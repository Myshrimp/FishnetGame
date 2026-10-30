using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FishNet.Component.Prediction;
using FishNet.Managing.Predicting;
using FishNet.Managing.Timing;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using NUnit.Framework;
using UnityEditor.TestTools;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class NetworkProjectilePredictionTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const float Step = 1f / 30f;
    private readonly List<GameObject> _objects = new();
    private Component _projectile;
    private Rigidbody _body;
    private Collider _collider;
    private NetworkObject _thrower;
    private Collider _throwerCollider;
    private Scene _scene;
    private Type _type;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        yield return new EnterPlayMode();
        _scene = SceneManager.CreateScene("PredictionRegression", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        _type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("NetworkProjectile")).First(t => t != null);
        GameObject root = CreateObject("Projectile");
        _body = root.AddComponent<Rigidbody>();
        _collider = root.AddComponent<SphereCollider>();
        NetworkObject nob = root.AddComponent<NetworkObject>();
        Transform view = new GameObject("View").transform;
        view.SetParent(root.transform, false);
        _projectile = root.AddComponent(_type);
        Set(_projectile, "_networkObjectCache", nob);
        Set(_projectile, "_graphicalObject", view);
        Set(_projectile, "_enableSmoothingLogs", false);
        Set(_projectile, "_gravity", 0f);
        Set(_projectile, "_airFrictionRate", 0f);
        GameObject managers = CreateObject("Managers");
        TimeManager time = managers.AddComponent<TimeManager>();
        time.SetTickRate(30);
        PredictionManager prediction = managers.AddComponent<PredictionManager>();
        typeof(NetworkObject).GetProperty("TimeManager", Flags).SetValue(nob, time);
        typeof(NetworkObject).GetProperty("PredictionManager", Flags).SetValue(nob, prediction);
        Activate(nob);

        GameObject thrower = CreateObject("Thrower");
        thrower.transform.position = new Vector3(-10f, 0f, 0f);
        _throwerCollider = thrower.AddComponent<BoxCollider>();
        _thrower = thrower.AddComponent<NetworkObject>();
        Activate(_thrower);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        foreach (GameObject item in _objects)
            if (item != null)
                Object.DestroyImmediate(item);
        _objects.Clear();
        AsyncOperation unload = SceneManager.UnloadSceneAsync(_scene);
        while (unload != null && !unload.isDone)
            yield return null;
        yield return new ExitPlayMode();
    }

    [Test]
    public void ReconcileRestoresHeldCollidersWithoutChangingPresentation()
    {
        object snapshot = Snapshot(1, 4, 0);
        Invoke("ApplyHolderState", false);
        Restore(snapshot);

        Assert.That(_body.isKinematic, Is.True);
        Assert.That(_collider.enabled, Is.False, "Historical held state must restore the collision rules, not only the Rigidbody.");
        Assert.That(Get(_projectile, "_presentationHeld"), Is.False, "Reconcile must not restart visual holding.");
    }

    [Test]
    public void ReleaseUsesRecordedPoseEvenWhenCurrentSyncVarSaysHeld()
    {
        var holder = (SyncVar<NetworkObject>)Get(_projectile, "_holder");
        holder.SetInitialValues(_thrower);
        Invoke("ApplyHolderState", true);
        Replicate(Input(2, 1, new Vector3(5f, 2f, 0f), Vector3.right * 18f, 3), 10);
        Simulate();

        Assert.That(_body.isKinematic, Is.False, "Present SyncVar state must not suppress a historical release.");
        Assert.That(_body.velocity.x, Is.EqualTo(18f).Within(0.001f));
        Assert.That(_body.position.x, Is.EqualTo(5.6f).Within(0.001f));
        Assert.That(_collider.enabled, Is.True);
    }

    [Test]
    public void DuplicateReleaseIsIgnoredButRollbackCanReplayIt()
    {
        object before = Snapshot(0, 0, 0);
        object release = Input(2, 1, Vector3.zero, Vector3.right * 18f, 3);
        Replicate(release, 10);
        Simulate();
        Replicate(release, 11);
        Simulate();
        Assert.That(_body.velocity.x, Is.EqualTo(18f).Within(0.001f), "Duplicate release must not apply another impulse.");
        Assert.That(_body.position.x, Is.EqualTo(1.2f).Within(0.001f), "Duplicate release must not reset the pose.");

        Restore(before);
        Replicate(release, 10, ReplicateState.Replayed | ReplicateState.Created);
        Simulate();
        Assert.That(_body.velocity.x, Is.EqualTo(18f).Within(0.001f), "Restoring a pre-release sequence must allow replay.");
        Assert.That(_body.position.x, Is.EqualTo(0.6f).Within(0.001f));
    }

    [Test]
    public void FutureInputCannotRepeatARelease()
    {
        object release = Input(2, 1, Vector3.zero, Vector3.right * 18f, 3);
        Replicate(release, 10);
        Simulate();
        Replicate(release, 100, ReplicateState.Replayed);
        Simulate();
        Assert.That(_body.velocity.x, Is.EqualTo(18f).Within(0.001f));
        Assert.That(_body.position.x, Is.EqualTo(1.2f).Within(0.001f));
    }

    [Test]
    public void CollisionGraceExpiresInSimulationStepsAndRewinds()
    {
        Replicate(Input(2, 1, Vector3.zero, Vector3.right, 2), 10);
        Simulate();
        Assert.That(Physics.GetIgnoreCollision(_collider, _throwerCollider), Is.True, "Spectators need the same collision grace as the server.");
        object afterRelease = SnapshotFromCurrent();
        Replicate(EmptyInput(), 11);
        Simulate();
        Assert.That(Physics.GetIgnoreCollision(_collider, _throwerCollider), Is.True);
        Replicate(EmptyInput(), 12);
        Simulate();
        Assert.That(Physics.GetIgnoreCollision(_collider, _throwerCollider), Is.False);
        Restore(afterRelease);
        Replicate(EmptyInput(), 11, ReplicateState.Replayed);
        Assert.That(Physics.GetIgnoreCollision(_collider, _throwerCollider), Is.True, "Rollback must restore the collision pair.");
        Invoke("OnStopNetwork");
        Assert.That(Physics.GetIgnoreCollision(_collider, _throwerCollider), Is.False, "Stopping must restore collision pairs.");
    }

    [Test]
    public void OldReleaseCannotUndoANewerPickup()
    {
        Replicate(Input(2, 1, Vector3.zero, Vector3.right * 18f, 3), 10);
        Simulate();
        Replicate(Input(1, 2, new Vector3(4f, 5f, 6f), Vector3.zero, 0), 11);
        Simulate();
        Replicate(Input(2, 1, Vector3.zero, Vector3.right * 18f, 3), 12);
        Simulate();
        Assert.That(_body.isKinematic, Is.True);
        Assert.That(_body.position, Is.EqualTo(new Vector3(4f, 5f, 6f)));
        Assert.That(Physics.GetIgnoreCollision(_collider, _throwerCollider), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ReplayReproducesCollisionWhenObstacleHistoryMatches(bool movingObstacle)
    {
        GameObject wall = CreateObject("Wall");
        wall.transform.position = new Vector3(3f, 0f, 0f);
        wall.transform.localScale = new Vector3(0.25f, 10f, 10f);
        Collider wallCollider = wall.AddComponent<BoxCollider>();
        PhysicMaterial material = new()
        {
            bounciness = 1f,
            bounceCombine = PhysicMaterialCombine.Maximum,
            dynamicFriction = 0f,
            staticFriction = 0f
        };
        wallCollider.sharedMaterial = material;
        _collider.sharedMaterial = material;
        Rigidbody obstacle = null;
        if (movingObstacle)
        {
            obstacle = wall.AddComponent<Rigidbody>();
            obstacle.isKinematic = true;
        }
        wall.SetActive(true);
        try
        {
            Replicate(Input(2, 1, Vector3.zero, Vector3.right * 18f, 0), 10);
            Simulate();
            object beforeBounce = SnapshotFromCurrent();
            for (uint tick = 11; tick <= 18; tick++)
            {
                if (obstacle != null)
                    obstacle.MovePosition(new Vector3(3f - (tick - 10) * 0.025f, 0f, 0f));
                Replicate(EmptyInput(), tick);
                Simulate();
            }
            Vector3 position = _body.position;
            Vector3 velocity = _body.velocity;
            Assert.That(velocity.x, Is.LessThan(-1f), "The trajectory must actually bounce.");

            if (obstacle != null)
                obstacle.position = new Vector3(3f, 0f, 0f);
            Restore(beforeBounce);
            for (uint tick = 11; tick <= 18; tick++)
            {
                if (obstacle != null)
                    obstacle.MovePosition(new Vector3(3f - (tick - 10) * 0.025f, 0f, 0f));
                Replicate(EmptyInput(), tick, ReplicateState.Replayed);
                Simulate();
            }
            Assert.That(Vector3.Distance(_body.position, position), Is.LessThan(0.02f));
            Assert.That(Vector3.Distance(_body.velocity, velocity), Is.LessThan(0.02f));
        }
        finally
        {
            Object.DestroyImmediate(material);
        }
    }

    [Test]
    public void QueuedReleaseChangesPhysicsOnlyWhenItsTickRuns()
    {
        _body.position = new Vector3(3f, 0f, 0f);
        Type motionType = _type.GetNestedType("ProjectileMotionState");
        Invoke("QueuePredictionTransition", Enum.ToObject(motionType, 2), _thrower, Vector3.right * 18f);
        Assert.That(_body.position.x, Is.EqualTo(3f));
        Assert.That(_body.velocity, Is.EqualTo(Vector3.zero));
        _thrower.transform.position = new Vector3(8f, 0f, 0f);
        object input = Invoke("BuildPredictionData");
        Replicate(input, 10);
        Simulate();
        Assert.That(_body.position.x, Is.EqualTo(9.45f).Within(0.001f), "Tick pose uses current logical holder plus hold offset, spawn offset and one physics step.");
        Assert.That(_body.velocity.x, Is.EqualTo(18f).Within(0.001f));
    }

    [Test]
    public void DuplicateReleaseDoesNotResetHitResolution()
    {
        object release = Input(2, 1, Vector3.zero, Vector3.right, 1);
        Replicate(release, 10);
        Simulate();
        Set(_projectile, "_hasHit", true);
        Replicate(release, 11);
        Assert.That(Get(_projectile, "_hasHit"), Is.True);
    }

    [Test]
    public void SwitchingThrowerRestoresTheOldCollisionPair()
    {
        Replicate(Input(2, 1, Vector3.zero, Vector3.right, 10), 10);
        Simulate();
        Collider previous = _throwerCollider;
        GameObject next = CreateObject("NextThrower");
        next.transform.position = Vector3.left * 20f;
        _throwerCollider = next.AddComponent<BoxCollider>();
        _thrower = next.AddComponent<NetworkObject>();
        Activate(_thrower);
        Replicate(Input(2, 2, Vector3.zero, Vector3.right, 10), 11);
        Simulate();
        Assert.That(Physics.GetIgnoreCollision(_collider, previous), Is.False);
        Assert.That(Physics.GetIgnoreCollision(_collider, _throwerCollider), Is.True);
    }

    [Test]
    public void ThrowerControllerRestartPreservesGraceBeforeTheNextPhysicsStep()
    {
        Object.DestroyImmediate(_throwerCollider);
        CharacterController controller = _thrower.gameObject.AddComponent<CharacterController>();
        _throwerCollider = controller;
        _thrower.transform.position = new Vector3(0.8f, 0f, 0f);
        Replicate(Input(2, 1, Vector3.zero, Vector3.right * 18f, 3), 10);
        Simulate();
        controller.enabled = false;
        controller.enabled = true;
        Replicate(EmptyInput(), 11, ReplicateState.Replayed);
        Assert.That(Physics.GetIgnoreCollision(_collider, controller), Is.True);
        Simulate();
        Assert.That(_body.velocity.x, Is.EqualTo(18f).Within(0.001f));
    }

    [Test]
    public void QueuedReleaseKeepsRecordedPoseWhenActorIsDestroyed()
    {
        _thrower.transform.position = new Vector3(8f, 0f, 0f);
        Type motionType = _type.GetNestedType("ProjectileMotionState");
        Invoke("QueuePredictionTransition", Enum.ToObject(motionType, 0), _thrower, Vector3.zero);
        Object.DestroyImmediate(_thrower.gameObject);
        object input = Invoke("BuildPredictionData");
        Replicate(input, 10);
        Simulate();
        Assert.That(Vector3.Distance(_body.position, new Vector3(8.55f, 1.1f, 0.75f)), Is.LessThan(0.001f));
        Assert.That(_body.isKinematic, Is.False);
    }

    [Test]
    public void HistoricalErrorDoesNotCountNormalFlightBetweenTicks()
    {
        Set(_projectile, "_enableSmoothingLogs", true);
        MethodInfo record = _type.GetMethod("RecordPredictionSample", Flags);
        Assert.That(record, Is.Not.Null, "Prediction diagnostics need a same-tick history sample.");
        _projectile.transform.position = Vector3.right;
        Physics.SyncTransforms();
        record.Invoke(_projectile, new object[] { (uint)100 });
        object snapshot = SnapshotFromCurrent();
        _projectile.transform.position = Vector3.right * 10f;
        PredictionManager manager = (PredictionManager)typeof(NetworkObject).GetProperty("PredictionManager", Flags)
            .GetValue(_projectile.GetComponent<NetworkObject>());
        typeof(PredictionManager).GetProperty("ClientStateTick", Flags).SetValue(manager, (uint)100);
        Restore(snapshot);
        Assert.That((float)Get(_projectile, "_maxHistoricalPositionError"), Is.LessThan(0.0001f));
        _projectile.transform.position = Vector3.right * 3f;
        Physics.SyncTransforms();
        object corrected = SnapshotFromCurrent();
        _projectile.transform.position = Vector3.right * 10f;
        Invoke("BeginPredictionReconcile", (uint)100, (uint)900);
        Restore(corrected);
        Invoke("EndPredictionReconcile", (uint)100, (uint)900);
        Assert.That((float)Get(_projectile, "_maxHistoricalPositionError"), Is.EqualTo(2f).Within(0.0001f));
        Assert.That((float)Get(_projectile, "_maxReplayPositionCorrection"), Is.EqualTo(7f).Within(0.0001f));
    }

    private object Input(int motion, uint sequence, Vector3 position, Vector3 velocity, uint grace)
    {
        object input = EmptyInput();
        SetIfPresent(input, "State", State(motion, sequence, grace));
        SetIfPresent(input, "HasTransition", true);
        SetIfPresent(input, "HasHeldPose", motion == 1);
        SetIfPresent(input, "Position", position);
        SetIfPresent(input, "Rotation", Quaternion.identity);
        Set(input, "ThrowVelocity", velocity);
        return input;
    }

    private object State(int motion, uint sequence, uint grace)
    {
        Type stateType = _type.GetNestedType("ProjectilePredictionState");
        if (stateType == null)
            return null;
        object state = Activator.CreateInstance(stateType);
        Set(state, "Motion", Enum.ToObject(stateType.GetField("Motion").FieldType, motion));
        Set(state, "Sequence", sequence);
        Set(state, "Holder", motion == 1 ? _thrower : null);
        Set(state, "Thrower", motion == 1 ? null : _thrower);
        Set(state, "IgnoreCollisionSteps", grace);
        return state;
    }

    private object Snapshot(int motion, uint sequence, uint grace)
    {
        bool wasKinematic = _body.isKinematic;
        _body.isKinematic = motion == 1;
        object result = MakeSnapshot(State(motion, sequence, grace));
        _body.isKinematic = wasKinematic;
        return result;
    }

    private object SnapshotFromCurrent()
    {
        return MakeSnapshot(_type.GetField("_predictionState", Flags)?.GetValue(_projectile));
    }

    private object MakeSnapshot(object state)
    {
        object result = Activator.CreateInstance(_type.GetNestedType("ProjectileReconcileData"));
        PredictionRigidbody saved = new();
        typeof(PredictionRigidbody).GetMethod("SetReconcileData", Flags).Invoke(saved,
            new object[] { new RigidbodyState(_body), new List<PredictionRigidbody.EntryData>() });
        Set(result, "Rigidbody", saved);
        SetIfPresent(result, "State", state);
        return result;
    }

    private void Restore(object snapshot)
    {
        Invoke("PerformReconcile___UL", snapshot, Channel.Unreliable);
        Physics.SyncTransforms();
    }

    private object EmptyInput() => Activator.CreateInstance(_type.GetNestedType("ProjectileReplicateData"));

    private void Replicate(object input, uint tick, ReplicateState state = ReplicateState.Ticked | ReplicateState.Created)
    {
        ((IReplicateData)input).SetTick(tick);
        Invoke("PerformReplicate___UL", input, state, Channel.Unreliable);
    }

    private void Simulate()
    {
        Physics.SyncTransforms();
        _scene.GetPhysicsScene().Simulate(Step);
    }

    private GameObject CreateObject(string name)
    {
        GameObject item = new(name);
        item.SetActive(false);
        SceneManager.MoveGameObjectToScene(item, _scene);
        _objects.Add(item);
        return item;
    }

    private static void Activate(NetworkObject nob)
    {
        nob.SetSceneId(42);
        nob.gameObject.SetActive(true);
        nob.SetSceneId(0);
    }

    private object Invoke(string name, params object[] args) => _type.GetMethod(name, Flags).Invoke(_projectile, args);

    private static object Get(object target, string name) => target.GetType().GetField(name, Flags).GetValue(target);

    private static void Set(object target, string name, object value)
    {
        for (Type type = target.GetType(); type != null; type = type.BaseType)
        {
            FieldInfo field = type.GetField(name, Flags);
            if (field == null)
                continue;
            field.SetValue(target, value);
            return;
        }
        Assert.Fail("Missing field: " + name);
    }

    private static void SetIfPresent(object target, string name, object value)
    {
        target.GetType().GetField(name, Flags)?.SetValue(target, value);
    }
}
