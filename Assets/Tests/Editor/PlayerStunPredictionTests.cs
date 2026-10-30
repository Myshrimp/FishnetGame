using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FishNet.Managing;
using FishNet.Managing.Server;
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

public class PlayerStunPredictionTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly List<GameObject> _objects = new();
    private Component _motor;
    private Component _carry;
    private Component _interaction;
    private NetworkObject _networkObject;
    private ServerManager _server;
    private TimeManager _time;
    private Scene _scene;
    private Type _type;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        yield return new EnterPlayMode();
        _scene = SceneManager.CreateScene("StunRegression", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        _type = FindType("PlayerControllerPrediction");
        GameObject root = CreateObject("Player");
        root.AddComponent<CharacterController>();
        _networkObject = root.AddComponent<NetworkObject>();
        _motor = root.AddComponent(_type);
        _carry = root.AddComponent(FindType("PlayerCarryController"));
        _interaction = root.AddComponent(FindType("PlayerProjectileController"));
        foreach (Component component in new[] { _motor, _carry, _interaction })
            SetField(component, "_networkObjectCache", _networkObject);
        _networkObject.SetSceneId(42);
        root.SetActive(true);
        _networkObject.SetSceneId(0);

        GameObject managers = CreateObject("Managers");
        LogAssert.Expect(LogType.Error, "SpawnablePrefabs is null on Managers. Select the NetworkManager in scene StunRegression and choose a prefabs file. Choosing DefaultPrefabObjects will automatically populate prefabs for you.");
        NetworkManager network = managers.AddComponent<NetworkManager>();
        _server = managers.AddComponent<ServerManager>();
        _time = managers.AddComponent<TimeManager>();
        _time.SetTickRate(30);
        SetProperty(network, "ServerManager", _server);
        SetProperty(_networkObject, "NetworkManager", network);
        SetProperty(_networkObject, "TimeManager", _time);
        SetProperty(_time, "NetworkManager", network);
        SetProperty(_time, "Tick", (uint)3000);
        SetField(_time, "_localTick", (uint)2400);
        SetProperty(_server, "Started", true);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (_server != null)
            SetProperty(_server, "Started", false);
        if (_networkObject != null)
            SetProperty(_networkObject, "NetworkManager", null);
        foreach (GameObject item in _objects)
            if (item != null)
                Object.DestroyImmediate(item);
        _objects.Clear();
        AsyncOperation unload = SceneManager.UnloadSceneAsync(_scene);
        while (unload != null && !unload.isDone)
            yield return null;
        yield return new ExitPlayMode();
    }

    [TestCase(2400u)]
    [TestCase(9000u)]
    public void HalfSecondHitBlocksExactlyFifteenStepsRegardlessOfInputTick(uint inputTick)
    {
        Hit(0.5f);
        for (uint i = 0; i < 15; i++)
        {
            Assert.That(IsStunned, Is.True);
            Assert.That(CanInteract, Is.False);
            Step(inputTick + i);
            Assert.That(_motor.transform.position.x, Is.EqualTo(0f).Within(0.0001f));
        }
        Assert.That(IsStunned, Is.False);
        Assert.That(CanInteract, Is.True);
        Step(inputTick + 15);
        Assert.That(_motor.transform.position.x, Is.GreaterThan(0.1f));
        Step(inputTick + 16);
        Assert.That(IsStunned, Is.False, "An expired unsigned counter must not underflow.");
    }

    [Test]
    public void ReconcileRestoresCountdownAndReplayReachesTheSamePosition()
    {
        Hit(0.5f);
        for (uint i = 0; i < 8; i++)
            Step(2400 + i);
        object snapshot = Snapshot(7, Vector3.zero);
        for (uint i = 8; i < 16; i++)
            Step(2400 + i);
        Vector3 expected = _motor.transform.position;
        Assert.That(IsStunned, Is.False);
        Assert.That(expected.x, Is.GreaterThan(0.1f));

        SetProperty(_server, "Started", false);
        Restore(snapshot);
        Assert.That(IsStunned, Is.True);
        for (uint i = 8; i < 16; i++)
            Step(2400 + i, ReplicateState.Replayed | ReplicateState.Created);
        Assert.That(IsStunned, Is.False);
        Assert.That(Vector3.Distance(_motor.transform.position, expected), Is.LessThan(0.0001f));
    }

    [Test]
    public void ZeroRemainingReconcileImmediatelyRestoresInteraction()
    {
        Hit(0.5f);
        Assert.That(CanInteract, Is.False);
        Restore(Snapshot(0, Vector3.zero));
        Assert.That(CanInteract, Is.True);
        Step(2400);
        Assert.That(_motor.transform.position.x, Is.GreaterThan(0.1f));
    }

    [TestCase(ReplicateState.Ticked)]
    [TestCase(ReplicateState.Replayed)]
    public void MissingOrFutureInputStillConsumesSimulationSteps(ReplicateState state)
    {
        Hit(0.1f);
        SetProperty(_server, "Started", false);
        for (uint i = 0; i < 3; i++)
            Step(2400 + i, state, Vector2.zero);
        Assert.That(IsStunned, Is.False);
        Assert.That(CanInteract, Is.True);
    }

    [Test]
    public void CarriedSimulationConsumesStunWithoutDecayingKnockback()
    {
        Hit(0.1f, Vector3.right * 10f);
        SetProperty(_server, "Started", false);
        NetworkObject carrier = CreateObject("Carrier").AddComponent<NetworkObject>();
        var holder = (SyncVar<NetworkObject>)GetField(_carry, "_carrier");
        holder.SetInitialValues(carrier);
        for (uint i = 0; i < 3; i++)
            Step(2400 + i);
        Assert.That(IsStunned, Is.False, "Carry must not freeze the stun countdown.");
        Assert.That(CanInteract, Is.False, "Being carried still blocks interaction after stun expires.");
        Assert.That(GetProperty(Status, "KnockbackVelocity"), Is.EqualTo(Vector3.right * 10f));
    }

    [Test]
    public void StoppedPredictionDoesNotConsumeStun()
    {
        Hit(0.1f);
        object state = GetField(_motor, "State");
        SetField(state, "StopPrediction", true);
        for (uint i = 0; i < 10; i++)
            Step(2400 + i);
        Assert.That(IsStunned, Is.True);
        SetField(state, "StopPrediction", false);
        for (uint i = 0; i < 3; i++)
        {
            Assert.That(IsStunned, Is.True);
            Step(2410 + i);
        }
        Assert.That(IsStunned, Is.False);
    }

    [TestCase(0f, 6)]
    [TestCase(-1f, 6)]
    [TestCase(0.1f, 6)]
    [TestCase(0.5f, 15)]
    public void RepeatedHitKeepsTheLongerRemainingDuration(float nextDuration, int remaining)
    {
        Hit(0.5f);
        for (uint i = 0; i < 9; i++)
            Step(2400 + i);
        Hit(nextDuration);
        for (uint i = 0; i < remaining; i++)
        {
            Assert.That(IsStunned, Is.True);
            Step(2409 + i);
        }
        Assert.That(IsStunned, Is.False);
    }

    [TestCase(0f, 0)]
    [TestCase(-1f, 0)]
    [TestCase(0.05f, 2)]
    public void HitDurationIsClampedAndRoundedUpToSimulationSteps(float duration, int steps)
    {
        Hit(duration);
        for (uint i = 0; i < steps; i++)
        {
            Assert.That(IsStunned, Is.True);
            Step(2400 + i);
        }
        Assert.That(IsStunned, Is.False);
    }

    [Test]
    public void NonServerCannotApplyHit()
    {
        SetProperty(_server, "Started", false);
        Hit(0.5f, Vector3.right * 10f);
        Assert.That(IsStunned, Is.False);
        Assert.That(GetProperty(Status, "KnockbackVelocity"), Is.EqualTo(Vector3.zero));
    }

    [Test]
    public void KnockbackContinuesAfterInputUnlocks()
    {
        Hit(0.1f, new Vector3(10f, 5f, 0f));
        for (uint i = 0; i < 3; i++)
            Step(2400 + i, input: Vector2.zero);
        Assert.That(IsStunned, Is.False);
        Assert.That(((Vector3)GetProperty(Status, "KnockbackVelocity")).x, Is.EqualTo(8.8f).Within(0.0001f));
        float before = _motor.transform.position.x;
        Step(2403);
        Assert.That(_motor.transform.position.x - before, Is.EqualTo(12.8f / 30f).Within(0.0001f));
    }

    private object Status => GetProperty(_motor, "Status");
    private bool IsStunned => (bool)GetProperty(_motor, "IsStunned");
    private bool CanInteract => (bool)_interaction.GetType().GetMethod("CanInteract", Flags).Invoke(_interaction, null);

    private void Hit(float duration, Vector3 velocity = default) => Invoke("ApplyHit", velocity, duration);

    private void Step(uint tick, ReplicateState state = ReplicateState.Ticked | ReplicateState.Created, Vector2? input = null)
    {
        object data = Activator.CreateInstance(_type.GetNestedType("ReplicateData"));
        SetField(data, "Input", input ?? Vector2.right);
        ((IReplicateData)data).SetTick(tick);
        Invoke("PerformReplicate___UL", data, state, Channel.Unreliable);
        SetProperty(_time, "Tick", _time.Tick + 1);
        SetField(_time, "_localTick", (uint)GetField(_time, "_localTick") + 1);
    }

    private object Snapshot(uint remaining, Vector3 knockback)
    {
        return Activator.CreateInstance(_type.GetNestedType("ReconcileData"),
            _motor.transform.localPosition, _motor.transform.localRotation,
            GetField(_motor, "_verticalVelocity"), GetProperty(_motor, "Stamina"),
            GetField(GetField(_motor, "State"), "IsCharging"), remaining, knockback);
    }

    private void Restore(object snapshot) => Invoke("PerformReconcile___UL", snapshot, Channel.Unreliable);
    private object Invoke(string name, params object[] args) => _type.GetMethod(name, Flags).Invoke(_motor, args);
    private static object GetProperty(object target, string name) => target.GetType().GetProperty(name, Flags).GetValue(target);
    private static void SetProperty(object target, string name, object value) => target.GetType().GetProperty(name, Flags).SetValue(target, value);
    private static object GetField(object target, string name) => target.GetType().GetField(name, Flags).GetValue(target);

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

    private static Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies()
        .Select(a => a.GetType("FaceSlapper.Network.Prediction." + name)).First(t => t != null);

    private GameObject CreateObject(string name)
    {
        GameObject item = new(name);
        item.SetActive(false);
        SceneManager.MoveGameObjectToScene(item, _scene);
        _objects.Add(item);
        return item;
    }
}
