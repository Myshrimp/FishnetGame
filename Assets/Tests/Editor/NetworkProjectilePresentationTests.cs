using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

public class NetworkProjectilePresentationTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly List<GameObject> _objects = new();
    private Component _projectile;
    private NetworkObject _networkObject;
    private NetworkObject _holder;
    private Transform _view;
    private Transform _holderView;
    private Transform _anchor;

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject item in _objects)
            Object.DestroyImmediate(item);
        _objects.Clear();
    }

    [TestCase(true)]
    [TestCase(false)]
    public void HoldingMovesOnlyTheViewDuringLateUpdate(bool prediction)
    {
        CreateProjectile(prediction);
        SetHeld(true);
        _projectile.transform.position = new Vector3(9f, 0f, 0f);
        _holderView.localPosition = new Vector3(-2f, 0f, 0f);

        Invoke(_projectile, "LateUpdate");

        Assert.That(_projectile.transform.position, Is.EqualTo(new Vector3(9f, 0f, 0f)));
        Assert.That(_view.position, Is.EqualTo(new Vector3(8f, 1f, 0.5f)));
        _holderView.localPosition = new Vector3(-1f, 0f, 0f);
        Invoke(_projectile, "LateUpdate");
        Assert.That(_view.position, Is.EqualTo(new Vector3(9f, 1f, 0.5f)));
        Assert.That(_projectile.transform.position, Is.EqualTo(new Vector3(9f, 0f, 0f)));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void LogicalHoldPoseDoesNotInheritPlayerSmoothing(bool prediction)
    {
        CreateProjectile(prediction);
        SetHeld(true);
        _holderView.localPosition = new Vector3(-2f, 0f, 0f);
        _holderView.localRotation = Quaternion.Euler(0f, 90f, 0f);

        Invoke(_projectile, "UpdateHeldTransform");

        Assert.That(Vector3.Distance(_projectile.transform.position, new Vector3(10f, 1f, 0.5f)), Is.LessThan(0.0001f));
        Assert.That(Quaternion.Angle(_projectile.transform.rotation, Quaternion.identity), Is.LessThan(0.001f));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ReleasingRestoresTheOriginalViewOffset(bool prediction)
    {
        CreateProjectile(prediction, new Vector3(0.2f, 0.3f, 0.4f));
        SetHeld(true);
        Invoke(_projectile, "LateUpdate");
        SetHeld(false);
        Invoke(_projectile, "SetLocalPose", new Vector3(20f, 2f, 3f), Quaternion.identity);
        Invoke(_projectile, "LateUpdate");

        Assert.That(_view.localPosition, Is.EqualTo(new Vector3(0.2f, 0.3f, 0.4f)));
        Assert.That(_projectile.GetComponent<Rigidbody>().isKinematic, Is.False);
        Assert.That(_projectile.GetComponent<Collider>().enabled, Is.True);
        _holderView.position += Vector3.one * 5f;
        Invoke(_projectile, "LateUpdate");
        Assert.That(_view.localPosition, Is.EqualTo(new Vector3(0.2f, 0.3f, 0.4f)));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void VisualSmoothingDoesNotStackRigidbodyInterpolation(bool prediction)
    {
        CreateProjectile(prediction);
        Assert.That(_projectile.GetComponent<Rigidbody>().interpolation, Is.EqualTo(RigidbodyInterpolation.None));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void EditorModeSelectionConfiguresPredictionBeforeSpawn(bool prediction)
    {
        CreateProjectile(prediction);
        SetField(_networkObject, "_enablePrediction", !prediction);
        Invoke(_projectile, "ValidateNetworkConfiguration");

        Assert.That(_networkObject.EnablePrediction, Is.EqualTo(prediction));
        Assert.That(_networkObject.EnableStateForwarding, Is.EqualTo(prediction));
        Assert.That(_networkObject.GetGraphicalObject(), Is.Null);
    }

    [TestCase(PhysicsMode.TimeManager, RigidbodyInterpolation.None)]
    [TestCase(PhysicsMode.Unity, RigidbodyInterpolation.Interpolate)]
    public void RpcInterpolationUsesTheActivePhysicsClock(PhysicsMode mode, RigidbodyInterpolation expected)
    {
        CreateProjectile(false);
        ConfigureManagers(mode);
        Invoke(_projectile, "OnStartNetwork");

        Assert.That(_projectile.GetComponent<Rigidbody>().interpolation, Is.EqualTo(expected));
        SetHeld(true);
        Assert.That(_projectile.GetComponent<Rigidbody>().interpolation, Is.EqualTo(RigidbodyInterpolation.None));
        SetHeld(false);
        Assert.That(_projectile.GetComponent<Rigidbody>().interpolation, Is.EqualTo(expected));
    }

    [Test]
    public void FlightSmoothsPositionAndRotationWithoutMovingPhysics()
    {
        StartPresentation(true, PhysicsMode.TimeManager);
        Assert.That(_projectile.GetType().GetField("_smoother", Flags).GetValue(_projectile), Is.Null);
        Invoke(_projectile, "LateUpdate");
        Vector3 targetPosition = new(2f, 3f, 4f);
        Quaternion targetRotation = Quaternion.Euler(40f, 100f, -20f);
        _projectile.transform.SetPositionAndRotation(targetPosition, targetRotation);

        Invoke(_projectile, "UpdateFlightSmoothing", 1f / 60f);

        Assert.That(Vector3.Distance(_view.position, Vector3.zero), Is.GreaterThan(0f));
        Assert.That(Vector3.Distance(_view.position, targetPosition), Is.GreaterThan(1f));
        Assert.That(Quaternion.Angle(_view.rotation, targetRotation), Is.GreaterThan(10f));
        Assert.That(_projectile.transform.position, Is.EqualTo(targetPosition));
        Assert.That(Quaternion.Angle(_projectile.transform.rotation, targetRotation), Is.LessThan(0.001f));
        for (int i = 0; i < 120; i++)
            Invoke(_projectile, "UpdateFlightSmoothing", 1f / 60f);
        Assert.That(Vector3.Distance(_view.position, targetPosition), Is.LessThan(0.001f));
        Assert.That(Quaternion.Angle(_view.rotation, targetRotation), Is.LessThan(0.1f));
    }

    [Test]
    public void CollisionCorrectionDoesNotSnapAndEventuallySettles()
    {
        StartPresentation(true, PhysicsMode.TimeManager);
        Invoke(_projectile, "LateUpdate");
        for (int i = 1; i <= 60; i++)
        {
            _projectile.transform.SetPositionAndRotation(
                Vector3.right * (i * 0.3f), Quaternion.Euler(i * 2f, i * 12f, 0f));
            Invoke(_projectile, "UpdateFlightSmoothing", 1f / 60f);
        }
        Vector3 before = _view.position;
        Quaternion beforeRotation = _view.rotation;
        Vector3 correctedPosition = before - Vector3.right * 3f;
        Quaternion correctedRotation = Quaternion.Euler(10f, -120f, 90f);
        _projectile.transform.SetPositionAndRotation(correctedPosition, correctedRotation);
        Invoke(_projectile, "UpdateFlightSmoothing", 1f / 60f);
        Assert.That(Vector3.Distance(_view.position, before), Is.LessThan(0.5f));
        Assert.That(Quaternion.Angle(_view.rotation, beforeRotation), Is.LessThan(25f));
        for (int i = 0; i < 120; i++)
            Invoke(_projectile, "UpdateFlightSmoothing", 1f / 60f);
        Assert.That(Vector3.Distance(_view.position, correctedPosition), Is.LessThan(0.001f));
        Assert.That(Quaternion.Angle(_view.rotation, correctedRotation), Is.LessThan(0.1f));
    }

    [TestCase(30)]
    [TestCase(60)]
    [TestCase(144)]
    public void TickSteppedFlightRemainsContinuousAtDifferentFrameRates(int frameRate)
    {
        StartPresentation(true, PhysicsMode.TimeManager);
        Invoke(_projectile, "LateUpdate");
        Vector3 previous = _view.position;
        Quaternion previousRotation = _view.rotation;
        for (int i = 1; i <= frameRate * 2; i++)
        {
            int tick = i * 30 / frameRate;
            _projectile.transform.SetPositionAndRotation(
                Vector3.right * (tick * 0.6f), Quaternion.Euler(0f, tick * 12f, 0f));
            Invoke(_projectile, "UpdateFlightSmoothing", 1f / frameRate);
            Assert.That(_view.position.x, Is.GreaterThanOrEqualTo(previous.x));
            Assert.That(Vector3.Distance(_view.position, previous), Is.LessThan(25f / frameRate));
            Assert.That(Quaternion.Angle(_view.rotation, previousRotation), Is.LessThan(500f / frameRate));
            if (i > frameRate)
                Assert.That(_view.position.x, Is.GreaterThan(previous.x), "Visual motion must continue between physics ticks.");
            previous = _view.position;
            previousRotation = _view.rotation;
        }
        Assert.That(_projectile.transform.position.x - _view.position.x, Is.InRange(1.5f, 2.8f));
    }

    [TestCase(30)]
    [TestCase(60)]
    [TestCase(144)]
    public void RotationCrosses180ByTheShortestPathAndConverges(int frameRate)
    {
        StartPresentation(true, PhysicsMode.TimeManager);
        _projectile.transform.rotation = Quaternion.Euler(0f, 170f, 0f);
        Invoke(_projectile, "LateUpdate");
        Quaternion target = Quaternion.Euler(0f, -170f, 0f);
        _projectile.transform.rotation = target;
        float previousError = 20f;
        for (int i = 0; i < frameRate; i++)
        {
            Invoke(_projectile, "UpdateFlightSmoothing", 1f / frameRate);
            float error = Quaternion.Angle(_view.rotation, target);
            Assert.That(error, Is.LessThanOrEqualTo(previousError + 0.01f));
            Assert.That(float.IsNaN(_view.rotation.w), Is.False);
            previousError = error;
        }
        Assert.That(previousError, Is.LessThan(0.1f));
    }

    [Test]
    public void ZeroDeltaTimePreservesPoseAndCanResumeSmoothing()
    {
        StartPresentation(true, PhysicsMode.TimeManager);
        Invoke(_projectile, "LateUpdate");
        _projectile.transform.SetPositionAndRotation(Vector3.one, Quaternion.Euler(20f, 90f, 45f));
        Invoke(_projectile, "UpdateFlightSmoothing", 0f);
        Assert.That(_view.position, Is.EqualTo(Vector3.zero));
        Assert.That(Quaternion.Angle(_view.rotation, Quaternion.identity), Is.LessThan(0.001f));
        Invoke(_projectile, "UpdateFlightSmoothing", 1f / 60f);
        Assert.That(_view.position.x, Is.InRange(0.001f, 0.5f));
        Assert.That(float.IsNaN(_view.rotation.w), Is.False);
    }

    [Test]
    public void TeleportThresholdUsesTargetStepInsteadOfAccumulatedVisualLag()
    {
        StartPresentation(true, PhysicsMode.TimeManager);
        SetField(_projectile, "_positionSmoothTime", 1f);
        SetField(_projectile, "_teleportDistance", 5f);
        Invoke(_projectile, "LateUpdate");
        for (int i = 1; i <= 20; i++)
        {
            _projectile.transform.SetPositionAndRotation(Vector3.right * i, Quaternion.Euler(0f, i * 5f, 0f));
            Invoke(_projectile, "UpdateFlightSmoothing", 1f / 60f);
        }
        Assert.That(Vector3.Distance(_view.position, _projectile.transform.position), Is.GreaterThan(5f));
        _projectile.transform.SetPositionAndRotation(Vector3.right * 100f, Quaternion.Euler(0f, 120f, 0f));
        Invoke(_projectile, "UpdateFlightSmoothing", 1f / 60f);
        Assert.That(_view.position, Is.EqualTo(_projectile.transform.position));
        Assert.That(Quaternion.Angle(_view.rotation, _projectile.transform.rotation), Is.LessThan(0.001f));
        Invoke(_projectile, "UpdateFlightSmoothing", 1f / 60f);
        Assert.That(_view.position, Is.EqualTo(_projectile.transform.position), "A teleport must clear damping velocity.");
        Assert.That(Quaternion.Angle(_view.rotation, _projectile.transform.rotation), Is.LessThan(0.001f));
    }

    [Test]
    public void DisabledTeleportThresholdSmoothsEvenLargeCorrections()
    {
        StartPresentation(true, PhysicsMode.TimeManager);
        Invoke(_projectile, "LateUpdate");
        _projectile.transform.position = Vector3.right * 100f;
        Invoke(_projectile, "UpdateFlightSmoothing", 1f / 60f);
        Assert.That(_view.position.x, Is.InRange(0.01f, 50f));
    }

    [Test]
    public void StoppingOrDisablingRestoresViewAndRestartDoesNotReuseVelocity()
    {
        StartPresentation(true, PhysicsMode.TimeManager);
        Invoke(_projectile, "LateUpdate");
        _projectile.transform.SetPositionAndRotation(Vector3.right, Quaternion.Euler(0f, 90f, 0f));
        Invoke(_projectile, "UpdateFlightSmoothing", 1f / 60f);
        Invoke(_projectile, "OnDisable");
        Assert.That(_view.parent, Is.EqualTo(_projectile.transform));
        Assert.That(_view.localPosition, Is.EqualTo(Vector3.zero));
        Invoke(_projectile, "LateUpdate");
        Assert.That(_view.parent, Is.Null);
        Invoke(_projectile, "UpdateFlightSmoothing", 1f / 60f);
        Assert.That(_view.position, Is.EqualTo(Vector3.right));
        Assert.That(Quaternion.Angle(_view.rotation, _projectile.transform.rotation), Is.LessThan(0.001f));
        Invoke(_projectile, "OnStopClient");
        Assert.That(_view.parent, Is.EqualTo(_projectile.transform));
        Invoke(_projectile, "LateUpdate");
        Assert.That(_view.parent, Is.EqualTo(_projectile.transform), "Stopped clients must not detach the view again.");
    }

    [TestCase(true)]
    [TestCase(false)]
    public void PositionAndRotationDampingTimesControlTheirOwnResponse(bool slowPosition)
    {
        StartPresentation(true, PhysicsMode.TimeManager);
        SetField(_projectile, "_positionSmoothTime", slowPosition ? 0.3f : 0.03f);
        SetField(_projectile, "_rotationSmoothTime", slowPosition ? 0.03f : 0.3f);
        Invoke(_projectile, "LateUpdate");
        _projectile.transform.SetPositionAndRotation(Vector3.right, Quaternion.Euler(0f, 90f, 0f));
        for (int i = 0; i < 3; i++)
            Invoke(_projectile, "UpdateFlightSmoothing", 1f / 60f);
        float positionProgress = _view.position.x;
        float rotationProgress = Quaternion.Angle(Quaternion.identity, _view.rotation) / 90f;
        Assert.That(slowPosition ? positionProgress : rotationProgress, Is.LessThan(0.15f));
        Assert.That(slowPosition ? rotationProgress : positionProgress, Is.GreaterThan(0.7f));
    }

    private void StartPresentation(bool prediction, PhysicsMode mode)
    {
        CreateProjectile(prediction);
        ConfigureManagers(mode);
        Invoke(_projectile, "OnStartNetwork");
        Invoke(_projectile, "OnStartClient");
    }

    [Test]
    public void RpcBodyPausesDuringOtherObjectsReplayAndRestoresVelocity()
    {
        CreateProjectile(false);
        PredictionManager manager = ConfigureManagers(PhysicsMode.TimeManager);
        Invoke(_projectile, "OnStartNetwork");
        Invoke(_projectile, "OnStartClient");
        Rigidbody body = _projectile.GetComponent<Rigidbody>();
        body.velocity = new Vector3(3f, 4f, 5f);
        body.angularVelocity = new Vector3(1f, 2f, 3f);

        Raise(manager, "OnPreReconcile");
        Assert.That(body.isKinematic, Is.True);
        Assert.That(body.detectCollisions, Is.False);
        Raise(manager, "OnPostReconcile");
        Assert.That(body.isKinematic, Is.False);
        Assert.That(body.detectCollisions, Is.True);
        Assert.That(body.velocity, Is.EqualTo(new Vector3(3f, 4f, 5f)));
        Assert.That(body.angularVelocity, Is.EqualTo(new Vector3(1f, 2f, 3f)));

        SetHeld(true);
        Raise(manager, "OnPreReconcile");
        Raise(manager, "OnPostReconcile");
        Assert.That(body.isKinematic, Is.True);
        SetHeld(false);
        Raise(manager, "OnPreReconcile");
        Invoke(_projectile, "OnStopClient");
        Assert.That(body.isKinematic, Is.False, "Stopping a client must not leave the body paused.");
        Raise(manager, "OnPreReconcile");
        Assert.That(body.isKinematic, Is.False, "Stopped clients must unsubscribe from replay.");
    }

    [Test]
    public void UnityPhysicsReleaseKeepsTheRenderedHandPoseOnItsFirstFrame()
    {
        CreateProjectile(false);
        ConfigureManagers(PhysicsMode.Unity);
        Invoke(_projectile, "OnStartNetwork");
        Invoke(_projectile, "OnStartClient");
        SetHeld(true);
        _holderView.localPosition = new Vector3(-2f, 0f, 0f);
        _holderView.localRotation = Quaternion.Euler(0f, 30f, 0f);
        Invoke(_projectile, "LateUpdate");
        Vector3 before = _view.position;
        Quaternion rotation = _view.rotation;
        SetHeld(false);
        Invoke(_projectile, "SetLocalPose", new Vector3(10f, 1f, 0.5f), Quaternion.identity);
        Invoke(_projectile, "LateUpdate");
        Assert.That(Vector3.Distance(_view.position, before), Is.LessThan(0.0001f));
        Assert.That(Quaternion.Angle(_view.rotation, rotation), Is.LessThan(0.001f));
    }

    [UnityTest]
    public IEnumerator PredictionReleaseKeepsLastRenderedPoseAfterTickMovesRoot()
    {
        yield return new EnterPlayMode();
        CreateProjectile(true);
        ConfigureManagers(PhysicsMode.TimeManager);
        SetField(_networkObject, "_enablePrediction", true);
        Invoke(_projectile, "OnStartNetwork");
        Invoke(_projectile, "OnStartClient");
        typeof(NetworkObject).GetProperty("ObjectId", Flags).SetValue(_networkObject, 43);
        typeof(NetworkObject).GetProperty("IsDeinitializing", Flags).SetValue(_networkObject, false);
        _holderView.localRotation = Quaternion.Euler(0f, 30f, 0f);
        ReplicatePresentationState(1, 1, new Vector3(10f, 1f, 0.5f));
        Invoke(_projectile, "LateUpdate");
        Vector3 before = _view.position;
        Quaternion rotation = _view.rotation;
        ReplicatePresentationState(2, 2, new Vector3(20f, 2f, 3f));
        Invoke(_projectile, "LateUpdate");
        Assert.That(Vector3.Distance(_view.position, before), Is.LessThan(0.0001f));
        Assert.That(Quaternion.Angle(_view.rotation, rotation), Is.LessThan(0.001f));
        Assert.That(_view.parent, Is.Null);
        Assert.That(_projectile.GetType().GetField("_smoothingActive", Flags).GetValue(_projectile), Is.True);
        Invoke(_projectile, "OnStopClient");
        typeof(NetworkObject).GetProperty("ObjectId", Flags).SetValue(_networkObject, NetworkObject.UNSET_OBJECTID_VALUE);
        TearDown();
        yield return new ExitPlayMode();
    }

    private void ReplicatePresentationState(int motion, uint sequence, Vector3 position)
    {
        Type type = _projectile.GetType();
        object state = Activator.CreateInstance(type.GetNestedType("ProjectilePredictionState"));
        SetField(state, "Motion", Enum.ToObject(type.GetNestedType("ProjectileMotionState"), motion));
        SetField(state, "Sequence", sequence);
        SetField(state, "Holder", motion == 1 ? _holder : null);
        object input = Activator.CreateInstance(type.GetNestedType("ProjectileReplicateData"));
        SetField(input, "State", state);
        SetField(input, "HasTransition", true);
        SetField(input, "HasHeldPose", motion == 1);
        SetField(input, "Position", position);
        SetField(input, "Rotation", Quaternion.identity);
        ((IReplicateData)input).SetTick(sequence);
        Invoke(_projectile, "PerformReplicate___UL", input,
            ReplicateState.Ticked | ReplicateState.Created, Channel.Unreliable);
    }

    [UnityTest]
    public IEnumerator RpcThrowSurvivesReplayBeforeTheNextPhysicsStep()
    {
        yield return new EnterPlayMode();
        Scene scene = SceneManager.CreateScene("ProjectilePhysicsTest", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        CreateProjectile(false);
        SceneManager.MoveGameObjectToScene(_projectile.gameObject, scene);
        _networkObject.SetSceneId(42);
        _projectile.gameObject.SetActive(true);
        _networkObject.SetSceneId(0);
        SetField(_projectile, "_airFrictionRate", 0f);
        PredictionManager manager = ConfigureManagers(PhysicsMode.TimeManager);
        SetField(_networkObject, "_enablePrediction", false);
        Invoke(_projectile, "OnStartNetwork");
        Invoke(_projectile, "OnStartClient");
        MethodInfo rpc = _projectile.GetType().GetMethods(Flags)
            .Single(m => m.Name.StartsWith("RpcLogic___ThrowObservers_"));
        rpc.Invoke(_projectile, new object[]
        {
            Vector3.zero, Quaternion.identity, new Vector3(18f, 0f, 0f), ForceMode.VelocityChange, null
        });
        Raise(manager, "OnPreReconcile");
        Raise(manager, "OnPostReconcile");
        Invoke(_projectile, "TimeManager_OnTick");
        scene.GetPhysicsScene().Simulate(1f / 30f);
        Rigidbody body = _projectile.GetComponent<Rigidbody>();
        Assert.That(body.velocity.x, Is.EqualTo(18f).Within(0.001f));
        Invoke(_projectile, "TimeManager_OnTick");
        scene.GetPhysicsScene().Simulate(1f / 30f);
        Assert.That(body.velocity.x, Is.EqualTo(18f).Within(0.001f), "Throw force must only be consumed once.");
        Invoke(_projectile, "OnStopClient");
        TearDown();
        yield return SceneManager.UnloadSceneAsync(scene);
        yield return new ExitPlayMode();
    }

    private PredictionManager ConfigureManagers(PhysicsMode mode)
    {
        GameObject managers = CreateObject("Managers");
        TimeManager timeManager = managers.AddComponent<TimeManager>();
        timeManager.SetTickRate(30);
        SetField(timeManager, "_physicsMode", mode);
        PredictionManager predictionManager = managers.AddComponent<PredictionManager>();
        typeof(NetworkObject).GetProperty("TimeManager", Flags).SetValue(_networkObject, timeManager);
        typeof(NetworkObject).GetProperty("PredictionManager", Flags).SetValue(_networkObject, predictionManager);
        Invoke(_projectile, "ValidateNetworkConfiguration");
        return predictionManager;
    }

    private static void Raise(PredictionManager manager, string name)
    {
        var handler = (Delegate)typeof(PredictionManager).GetField(name, Flags).GetValue(manager);
        handler?.DynamicInvoke((uint)1, (uint)1);
    }

    [UnityTest]
    public IEnumerator RepeatedFlightAndHoldRestoresViewParentAndScale()
    {
        yield return new EnterPlayMode();
        foreach (bool prediction in new[] { true, false })
        {
            Vector3 offset = new(0.2f, 0.3f, 0.4f);
            CreateProjectile(prediction, offset);
            _projectile.transform.localScale = Vector3.one * 7f;
            ConfigureManagers(PhysicsMode.TimeManager);
            SetField(_networkObject, "_enablePrediction", prediction);
            Invoke(_projectile, "OnStartNetwork");
            Invoke(_projectile, "OnStartClient");

            for (int cycle = 0; cycle < 3; cycle++)
            {
                Invoke(_projectile, "LateUpdate");
                Assert.That(_view.parent, Is.Null);
                Assert.That(_view.lossyScale, Is.EqualTo(Vector3.one * 7f));
                SetHeld(true);
                Assert.That(_view.parent, Is.EqualTo(_projectile.transform));
                Invoke(_projectile, "LateUpdate");
                Vector3 heldPosition = _view.position;
                Quaternion heldRotation = _view.rotation;
                SetHeld(false);
                Invoke(_projectile, "SetLocalPose", new Vector3(20f + cycle, 2f, 3f), Quaternion.identity);
                Invoke(_projectile, "LateUpdate");
                Assert.That(_view.parent, Is.Null);
                Assert.That(Vector3.Distance(_view.position, heldPosition), Is.LessThan(0.0001f));
                Assert.That(Quaternion.Angle(_view.rotation, heldRotation), Is.LessThan(0.001f));
            }

            Invoke(_projectile, "OnStopClient");
            Assert.That(_view.parent, Is.EqualTo(_projectile.transform));
            Assert.That(Vector3.Distance(_view.localPosition, offset), Is.LessThan(0.0001f));
            Assert.That(_view.localScale, Is.EqualTo(Vector3.one));
            yield return null;
        }
        TearDown();
        yield return new ExitPlayMode();
    }

    private void CreateProjectile(bool prediction, Vector3 viewOffset = default)
    {
        Type projectileType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("NetworkProjectile")).First(t => t != null);
        GameObject root = CreateObject("Projectile");
        root.AddComponent<Rigidbody>();
        root.AddComponent<BoxCollider>();
        _networkObject = root.AddComponent<NetworkObject>();
        _view = new GameObject("View").transform;
        _view.SetParent(root.transform, false);
        _view.localPosition = viewOffset;
        _networkObject.SetGraphicalObject(_view);
        _projectile = root.AddComponent(projectileType);
        SetField(_projectile, "_networkObjectCache", _networkObject);
        SetField(_projectile, "_usePrediction", prediction);
        SetField(_projectile, "_enableSmoothingLogs", false);
        projectileType.GetField("_graphicalObject", Flags)?.SetValue(_projectile, _view);
        MethodInfo awake = projectileType.GetMethods(Flags)
            .FirstOrDefault(m => m.Name.StartsWith("Awake_UserLogic_")) ?? projectileType.GetMethod("Awake", Flags);
        awake.Invoke(_projectile, null);

        GameObject holder = CreateObject("Holder");
        holder.transform.position = new Vector3(10f, 0f, 0f);
        _holder = holder.AddComponent<NetworkObject>();
        typeof(NetworkObject).GetProperty("ObjectId", Flags).SetValue(_holder, 42);
        typeof(NetworkObject).GetProperty("IsDeinitializing", Flags).SetValue(_holder, false);
        _holderView = new GameObject("PlayerView").transform;
        _holderView.SetParent(holder.transform, false);
        _holder.SetGraphicalObject(_holderView);
        _anchor = new GameObject("HandSocket").transform;
        _anchor.SetParent(_holderView, false);
        _anchor.localPosition = new Vector3(0f, 1f, 0.5f);
        Type controllerType = projectileType.Assembly.GetType("FaceSlapper.Network.Prediction.PlayerProjectileController");
        Component controller = holder.AddComponent(controllerType);
        SetField(controller, "_holdAnchor", _anchor);
    }

    private GameObject CreateObject(string name)
    {
        GameObject result = new(name);
        result.SetActive(false);
        _objects.Add(result);
        return result;
    }

    private void SetHeld(bool held)
    {
        var holder = (SyncVar<NetworkObject>)_projectile.GetType().GetField("_holder", Flags).GetValue(_projectile);
        holder.SetInitialValues(held ? _holder : null);
        Invoke(_projectile, "ApplyHolderState", held);
    }

    private static void SetField(object target, string name, object value)
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

    private static object Invoke(object target, string name, params object[] args)
    {
        MethodInfo method = target.GetType().GetMethod(name, Flags);
        Assert.That(method, Is.Not.Null, "Missing behavior: " + name);
        return method.Invoke(target, args);
    }
}
