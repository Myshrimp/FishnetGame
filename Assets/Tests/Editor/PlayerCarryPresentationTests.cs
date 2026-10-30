using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FishNet.Component.Transforming;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Server;
using FishNet.Managing.Timing;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Object.Synchronizing;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

#pragma warning disable CS0618
public class PlayerCarryPresentationTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly List<GameObject> _objects = new();
    private Component _carry;
    private NetworkObject _passenger;
    private NetworkObject _carrier;
    private Transform _view;
    private Transform _carrierView;
    private TimeManager _time;
    private TransformTickSmoother _smoother;

    [SetUp]
    public void SetUp()
    {
        GameObject managers = CreateObject("CarryTestManagers");
        NetworkManager network = managers.AddComponent<NetworkManager>();
        ServerManager server = managers.AddComponent<ServerManager>();
        _time = managers.AddComponent<TimeManager>();
        _time.SetTickRate(30);
        SetProperty(network, "ServerManager", server);
        SetProperty(_time, "NetworkManager", network);
        SetField(_time, "_localTick", (uint)100);

        _carrier = CreateObject("Carrier").AddComponent<NetworkObject>();
        SetProperty(_carrier, "ObjectId", 42);
        SetProperty(_carrier, "IsDeinitializing", false);
        _carrierView = new GameObject("CarrierView").transform;
        _carrierView.SetParent(_carrier.transform, false);
        _carrier.SetGraphicalObject(_carrierView);
    }

    [TearDown]
    public void TearDown()
    {
        if (_carry != null)
            InvokeOptional(_carry, "OnStopClient");
        if (_passenger != null)
            SetProperty(_passenger, "PredictionSmoother", null);
        _smoother?.Deinitialize();
        _smoother = null;
        foreach (GameObject item in _objects)
            if (item != null)
                Object.DestroyImmediate(item);
        _objects.Clear();
    }

    [Test]
    public void PassengerViewStaysInFrontEvenWhenItsOwnSmoothingIsBehind()
    {
        CreatePassenger();
        _carrier.transform.position = Vector3.forward * 10f;
        Invoke(_carry, "FollowCarrier");
        Assert.That(_passenger.transform.position.z, Is.EqualTo(11.3f).Within(0.0001f));
        _carrierView.position = Vector3.forward * 9f;
        _view.position = Vector3.forward * 4f;
        Render();
        Assert.That(_view.position.z, Is.EqualTo(10.3f).Within(0.0001f));
        Assert.That(_passenger.transform.position.z, Is.EqualTo(11.3f).Within(0.0001f),
            "Presentation must not write to the predicted root.");
    }

    [TestCase(30)]
    [TestCase(60)]
    [TestCase(144)]
    public void FastChargeAndTurnKeepTheVisualGapAcrossInterpolationUpdates(int fps)
    {
        CreatePassenger(withSmoother: true);
        int lastTick = 0;
        for (int frame = 1; frame <= fps * 2; frame++)
        {
            float elapsed = (float)frame / fps;
            int tick = Mathf.FloorToInt(elapsed * 30f);
            if (tick > lastTick)
            {
                _smoother.OnPreTick();
                _carrier.transform.position = new Vector3(0f, 0f, tick * 20f / 30f);
                Invoke(_carry, "FollowCarrier");
                _smoother.OnPostTick((uint)(100 + tick));
                lastTick = tick;
            }
            SetField(_smoother, "_startFrame", 0);
            Invoke(_smoother, "MoveToTarget", 1f / fps);
            _carrierView.SetPositionAndRotation(new Vector3(0f, 0f, elapsed * 20f - 0.6f),
                Quaternion.Euler(0f, elapsed * 90f, 0f));
            Vector3 root = _passenger.transform.position;
            Render();
            Vector3 gap = _view.position - _carrierView.position;
            Assert.That(Vector3.Dot(gap, _carrierView.forward), Is.EqualTo(1.3f).Within(0.0001f));
            Assert.That(Vector3.Cross(gap, _carrierView.forward).magnitude, Is.LessThan(0.0001f));
            Assert.That(_passenger.transform.position, Is.EqualTo(root));
        }
    }

    [Test]
    public void CarryDistanceIsWorldSpaceAndPreservesPassengerVisualOffset()
    {
        CreatePassenger(new Vector3(0.2f, 0.4f, 0.1f));
        _passenger.transform.localScale = Vector3.one * 2f;
        _carrierView.localScale = Vector3.one * 7f;
        _carrierView.SetPositionAndRotation(new Vector3(3f, 2f, 1f), Quaternion.Euler(0f, 90f, 0f));
        Render();
        Assert.That(Vector3.Distance(_view.position, new Vector3(4.7f, 2.8f, 1.2f)), Is.LessThan(0.0001f));
        Assert.That(_view.localScale, Is.EqualTo(Vector3.one));
    }

    [Test]
    public void ReleaseClearsOldInterpolationAndKeepsLastRenderedPose()
    {
        CreatePassenger(withSmoother: true);
        _smoother.OnPreTick();
        _passenger.transform.position = Vector3.forward;
        _smoother.OnPostTick(101);
        _carrierView.position = Vector3.forward * 10f;
        Render();
        Pose last = new(_view.position, _view.rotation);
        SetCarrier(null);
        _view.position = Vector3.back * 5f;
        Render();
        Assert.That(Vector3.Distance(_view.position, last.position), Is.LessThan(0.0001f));
        Assert.That(Quaternion.Angle(_view.rotation, last.rotation), Is.LessThan(0.001f));
        SetField(_smoother, "_startFrame", 0);
        Invoke(_smoother, "MoveToTarget", 1f);
        Assert.That(Vector3.Distance(_view.position, last.position), Is.LessThan(0.0001f),
            "The pre-release interpolation queue must not pull the passenger backward.");

        _smoother.OnPreTick();
        _passenger.transform.position = Vector3.forward * 12f;
        _smoother.OnPostTick(102);
        _smoother.OnPreTick();
        _smoother.OnPostTick(103);
        SetField(_smoother, "_startFrame", 0);
        Invoke(_smoother, "MoveToTarget", 1f);
        Assert.That(_view.position.z, Is.EqualTo(12f).Within(0.0001f),
            "Normal player smoothing must resume after release.");
    }

    [TestCase("OnStopClient")]
    [TestCase("OnDisable")]
    public void StoppingRestoresTheVisualOffset(string callback)
    {
        Vector3 offset = new(0.2f, 0.4f, 0.1f);
        CreatePassenger(offset);
        _carrierView.position = Vector3.forward * 10f;
        Render();
        InvokeOptional(_carry, callback);
        Assert.That(Vector3.Distance(_view.localPosition, offset), Is.LessThan(0.0001f));
    }

    [Test]
    public void MissingCarrierViewFallsBackToItsLogicalRoot()
    {
        CreatePassenger();
        _carrier.SetGraphicalObject(null);
        _carrier.transform.SetPositionAndRotation(Vector3.right * 3f, Quaternion.Euler(0f, 90f, 0f));
        Render();
        Assert.That(Vector3.Distance(_view.position, Vector3.right * 4.3f), Is.LessThan(0.0001f));
    }

    [Test]
    public void DespawnedCarrierStopsVisualFollowing()
    {
        CreatePassenger();
        _carrierView.position = Vector3.forward * 10f;
        Render();
        SetProperty(_carrier, "IsDeinitializing", true);
        Render();
        Assert.That(_view.localPosition, Is.EqualTo(Vector3.zero));
    }

    private void CreatePassenger(Vector3 offset = default, bool withSmoother = false)
    {
        GameObject root = CreateObject("Passenger");
        root.AddComponent<CharacterController>();
        _passenger = root.AddComponent<NetworkObject>();
        _view = new GameObject("PassengerView").transform;
        _view.SetParent(root.transform, false);
        _view.localPosition = offset;
        _passenger.SetGraphicalObject(_view);
        SetProperty(_passenger, "TimeManager", _time);
        SetProperty(_passenger, "Owner", new NetworkConnection());
        Type motorType = FindType("PlayerControllerPrediction");
        Component motor = root.AddComponent(motorType);
        _carry = root.AddComponent(FindType("PlayerCarryController"));
        SetField(motor, "_networkObjectCache", _passenger);
        SetField(_carry, "_networkObjectCache", _passenger);
        Awake(motor);
        Awake(_carry);
        SetProperty(_passenger, "IsClientInitialized", true);
        SetCarrier(_carrier);
        if (withSmoother)
        {
            _smoother = new TransformTickSmoother();
            _smoother.InitializeNetworked(_passenger, _view, false, MoveRates.UNSET_VALUE, 1f / 30f,
                1, TransformPropertiesFlag.Everything, 6, TransformPropertiesFlag.Everything, AdaptiveInterpolationType.Off);
            SetProperty(_passenger, "PredictionSmoother", _smoother);
        }
    }

    private void SetCarrier(NetworkObject carrier) =>
        ((SyncVar<NetworkObject>)_carry.GetType().GetField("_carrier", Flags).GetValue(_carry)).Value = carrier;

    private void Render() => InvokeOptional(_carry, "LateUpdate");
    private static void InvokeOptional(object target, string method) => target.GetType().GetMethod(method, Flags)?.Invoke(target, null);
    private static object Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, Flags).Invoke(target, args);
    private static void SetProperty(object target, string name, object value) => target.GetType().GetProperty(name, Flags).SetValue(target, value);

    private static void Awake(Component component)
    {
        MethodInfo method = component.GetType().GetMethods(Flags).FirstOrDefault(m => m.Name.StartsWith("Awake_UserLogic_"))
            ?? component.GetType().GetMethod("Awake", Flags);
        method.Invoke(component, null);
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

    private static Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies()
        .Select(a => a.GetType("FaceSlapper.Network.Prediction." + name)).First(t => t != null);

    private GameObject CreateObject(string name)
    {
        GameObject result = new(name);
        result.SetActive(false);
        _objects.Add(result);
        return result;
    }
}
#pragma warning restore CS0618
