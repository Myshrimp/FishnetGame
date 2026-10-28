using System;
using System.Linq;
using System.Reflection;
using FaceSlapper.Networking;
using UnityEngine;

internal static class RpcChannelTests
{
    private static int _passed;
    private static int _failed;

    private static int Main()
    {
        foreach (string direction in new[] { "Server", "Observers", "Target" })
        {
            Check(direction + " 默认可靠且保留参数", () => SendAndVerify(direction, null, Payload()));
            Check(direction + " 可靠通道", () => SendAndVerify(direction, "Reliable", Payload()));
            Check(direction + " 不可靠通道", () => SendAndVerify(direction, "Unreliable", Payload()));
            Check(direction + " 不可靠无参数", () => SendAndVerify(direction, "Unreliable", Array.Empty<object>()));
            Check(direction + " 旧调用的零值和枚举不被当作通道", () =>
                SendAndVerify(direction, null, new object[] { 0, DayOfWeek.Tuesday }));
        }
        foreach (string direction in new[] { "Observers", "Target" })
            Check(direction + " 客户端禁止发送", () =>
            {
                var probe = CreateProbe(out var capture);
                Net.IsServer = false;
                Send(probe, direction, "Unreliable", Payload());
                Assert(capture.Calls == 0, "客户端越权发送了服务器 RPC");
            });
        Check("不可靠 ServerRpc 保留接收派发及发送者", () =>
        {
            var probe = CreateProbe(out var capture);
            Send(probe, "Server", "Unreliable", new object[] { 42, "hello" });
            probe.NetObject.DispatchRpc(capture.Method, NetSerializer.ReadArgs(capture.Payload), 17);
            Assert(probe.Received == "42:hello:17", "接收参数或发送者丢失");
            Assert(probe.NetObject.RpcSenderClientId == -1, "派发后未清理发送者");
        });
        Console.WriteLine($"RPC 通道测试：{_passed} 通过，{_failed} 失败");
        return _failed == 0 ? 0 : 1;
    }

    private static object[] Payload() => new object[] { 42, "hello", new Vector3(1, 2, 3), true };

    private static RpcProbe CreateProbe(out RecordingBridge capture)
    {
        Net.IsServer = true;
        Net.IsClient = true;
        var bridge = DispatchProxy.Create<INetObjectBridge, RecordingBridge>();
        capture = (RecordingBridge)(object)bridge;
        var obj = new NetObject();
        obj.AttachBridge(bridge);
        var probe = new RpcProbe();
        probe.Components[typeof(NetObject)] = obj;
        probe.Initialize();
        return probe;
    }

    private static void SendAndVerify(string direction, string channel, object[] args)
    {
        var probe = CreateProbe(out var capture);
        Send(probe, direction, channel, args);
        Assert(capture.Calls == 1, "RPC 没有发送一次");
        Assert(capture.Direction == "Send" + direction + "Rpc", "发送方向错误");
        Assert(capture.Channel == (channel ?? "Reliable"), "传输通道错误");
        Assert(capture.Method == "Receive", "方法名丢失");
        if (direction == "Target") Assert(capture.ClientId == 23, "目标客户端丢失");
        object[] decoded = NetSerializer.ReadArgs(capture.Payload);
        Assert(decoded.Length == args.Length, "通道被序列化成业务参数");
        for (int i = 0; i < args.Length; i++)
            Assert(Equals(decoded[i], args[i] is Enum ? Convert.ToInt32(args[i]) : args[i]), "业务参数发生变化");
    }

    private static void Send(RpcProbe probe, string direction, string channel, object[] args)
    {
        if (channel == null)
        {
            probe.SendLegacy(direction, args);
            return;
        }
        Type channelType = typeof(NetBehaviour).Assembly.GetType("FaceSlapper.Networking.NetChannel");
        Assert(channelType != null, "缺少后端无关的 NetChannel");
        MethodInfo method = typeof(NetBehaviour).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .SingleOrDefault(m => m.Name == "Send" + direction + "Rpc" && m.GetParameters()[0].ParameterType == channelType);
        Assert(method != null, "缺少显式通道重载：" + direction);
        object selected = Enum.Parse(channelType, channel);
        method.Invoke(probe, direction == "Target"
            ? new object[] { selected, 23, "Receive", args }
            : new object[] { selected, "Receive", args });
    }

    private static void Assert(bool success, string message)
    {
        if (!success) throw new Exception(message);
    }

    private static void Check(string name, Action test)
    {
        try { test(); _passed++; Console.WriteLine("通过：" + name); }
        catch (Exception e) { _failed++; Console.WriteLine("失败：" + name + " — " + e.GetBaseException().Message); }
    }

    private sealed class RpcProbe : NetBehaviour
    {
        public string Received;
        public void Initialize() => Awake();

        public void SendLegacy(string direction, object[] args)
        {
            if (direction == "Server") SendServerRpc("Receive", args);
            else if (direction == "Observers") SendObserversRpc("Receive", args);
            else SendTargetRpc(23, "Receive", args);
        }

        [NetRpc]
        private void Receive(int value, string text) => Received = $"{value}:{text}:{NetObject.RpcSenderClientId}";
    }
}

public class RecordingBridge : DispatchProxy
{
    public int Calls;
    public string Direction, Channel, Method;
    public int ClientId;
    public byte[] Payload;

    protected override object Invoke(MethodInfo targetMethod, object[] args)
    {
        Calls++;
        Direction = targetMethod.Name;
        int offset = Direction == "SendTargetRpc" ? 1 : 0;
        if (offset == 1) ClientId = (int)args[0];
        Method = (string)args[offset];
        Payload = (byte[])args[offset + 1];
        Channel = args.Length > offset + 2 ? args[offset + 2].ToString() : "Reliable";
        return null;
    }
}
