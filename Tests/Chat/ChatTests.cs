using System;
using System.Collections.Generic;
using System.Reflection;
using FaceSlapper.Core;
using FaceSlapper.FrameSync.Separated.Test;
using FaceSlapper.FrameSync.Separated.Services;
using FaceSlapper.FrameSync.Separated.UI;
using FaceSlapper.Networking;
using Newtonsoft.Json;
using UnityEngine;

internal static class ChatTests
{
    private static int _passed, _failed;

    private static int Main()
    {
        Check("服务注册时保存角色并初始化", () =>
        {
            var source = new ServerMain();
            var service = new ProbeService();
            service.OnAddService(source, true);
            Assert(service.Server == source && service.ServerRole && service.AwakeCount == 1, "服务端角色或初始化缺失");
        });
        Check("客户端服务可初始化并释放", () =>
        {
            var source = new GameMain();
            var service = new ProbeService();
            service.OnAddService(source, false);
            Assert(service.Client == source && !service.ServerRole && service.AwakeCount == 1, "客户端初始化缺失");
            service.OnRemoveService(source);
            service.OnRemoveService(source);
            Assert(service.Client == null && service.DestroyCount == 1, "释放必须恰好执行一次");
        });
        Check("远端公共聊天规范化发送者并且 Host 只展示一次", () =>
        {
            var (server, client, view) = Setup();
            server.NetObject.RpcSenderClientId = 11;
            Invoke(server, "CmdServerReceiveData", Packet(" hello ", 999));
            Assert(Net.Sent.Count == 1 && Net.Sent[0].Direction == "Observers", "没有恰好广播一次");
            var msg = JsonConvert.DeserializeObject<DataMsg>((string)Net.Sent[0].Args[0]);
            var head = JsonConvert.DeserializeObject<MsgHead>(msg.head);
            Assert(head.fromPlayer == 11 && head.isBroadcast, "服务器信任了伪造的发送者");
            Assert(view.Messages.Count == 1 && view.Messages[0] == "[User 11]:hello", "Host 重复显示或内容错误");
        });
        Check("Host 本地发送使用本机连接身份", () =>
        {
            var (server, client, view) = Setup();
            Invoke(server, "CmdServerReceiveData", Packet("host"));
            Assert(view.Messages.Count == 1 && view.Messages[0] == "[User 7]:host", "Host 发送者身份错误");
        });
        Check("异常 JSON、空内容、超长内容和私聊请求不广播", () =>
        {
            var (server, client, view) = Setup();
            foreach (string packet in new[] { "{", "null", "[]", Packet(" "), Packet(new string('x', 257)), Packet("secret", broadcast: false) })
                Invoke(server, "CmdServerReceiveData", packet);
            Invoke(server, "CmdServerReceiveData", JsonConvert.SerializeObject(new DataMsg("{", "{}")));
            Assert(Net.Sent.Count == 0 && view.Messages.Count == 0, "非法消息被转发");
        });
        Check("未连接身份无法发送", () =>
        {
            var (server, client, view) = Setup();
            server.NetObject.RpcSenderClientId = 999;
            Invoke(server, "CmdServerReceiveData", Packet("fake"));
            Assert(Net.Sent.Count == 0, "不存在的连接被允许发言");
        });
        Check("远端不能调用客户端入口伪造聊天展示", () =>
        {
            var (server, client, view) = Setup();
            server.NetObject.RpcSenderClientId = 11;
            Invoke(server, "CmdClientReceiveData", Packet("forged", 999));
            Assert(view.Messages.Count == 0 && Net.Sent.Count == 0, "远端绕过服务端校验显示了消息");
        });
        Check("256 字符边界可发送且过大封包被拒绝", () =>
        {
            var (server, client, view) = Setup();
            Invoke(server, "CmdServerReceiveData", Packet(new string('x', 256)));
            Assert(Net.Sent.Count == 1 && view.Messages.Count == 1, "长度上限的合法消息被拒绝");
            Invoke(server, "CmdServerReceiveData", new string(' ', 8193));
            Invoke(server, "CmdServerReceiveData", JsonConvert.SerializeObject(new DataMsg("{}", "[]")));
            Assert(Net.Sent.Count == 1, "过大或错误封包被接受");
        });
        Check("服务停止再启动不会残留或重复订阅", () =>
        {
            var (server, client, view) = Setup();
            server.OnNetDespawnServer();
            Invoke(server, "CmdServerReceiveData", Packet("stopped"));
            Assert(Net.Sent.Count == 0, "停止后仍然广播");
            server.OnNetSpawnServer();
            Invoke(server, "CmdServerReceiveData", Packet("restarted"));
            Assert(Net.Sent.Count == 1 && view.Messages.Count == 1, "重启后重复广播");
            Invoke(client, "OnDestroy");
            Invoke(server, "CmdServerReceiveData", Packet("destroyed"));
            Assert(view.Messages.Count == 1, "客户端销毁后订阅未释放");
        });
        Check("客户端公共聊天等待回显且离线发送失败", () =>
        {
            var (server, client, view) = Setup();
            PropertyInfo chat = typeof(GameMain).GetProperty("Chat");
            Assert(chat != null, "缺少客户端聊天服务");
            object service = chat.GetValue(client);
            MethodInfo send = service.GetType().GetMethod("TrySendMessage");
            Assert(send != null, "缺少聊天发送入口");
            object[] args = { "client", null };
            Net.IsServer = false;
            Assert((bool)send.Invoke(service, args), "在线发送失败");
            Assert(Net.Sent.Count == 1 && Net.Sent[0].Direction == "Server", "客户端没有发起 ServerRpc");
            Assert(view.Messages.Count == 0, "收到服务器回显前就显示消息");
            Net.IsClient = false;
            Assert(!(bool)send.Invoke(service, new object[] { "offline", null }), "离线发送成功");
            Assert(Net.Sent.Count == 1, "离线状态仍然发出 RPC");
        });
        Check("Host 连接尚未分配 ID 时保留待发送消息", () =>
        {
            var (server, client, view) = Setup();
            Net.LocalClientId = -1;
            Assert(!client.Chat.TrySendMessage("connecting", out string error), "连接未完成却报告发送成功");
            Assert(Net.Sent.Count == 0 && !string.IsNullOrEmpty(error), "未连接身份发送了消息");
        });
        Check("客户端聊天对象尚未就绪时不发送", () =>
        {
            var (server, client, view) = Setup();
            server.OnNetDespawnClient();
            Assert(!client.Chat.TrySendMessage("not-ready", out string error), "客户端对象已退出却报告发送成功");
            Assert(Net.Sent.Count == 0 && !string.IsNullOrEmpty(error), "未就绪时发出了 RPC");
        });
        Console.WriteLine($"聊天测试：{_passed} 通过，{_failed} 失败");
        return _failed == 0 ? 0 : 1;
    }

    private static void Check(string name, Action test)
    {
        EventBus.Clear();
        GameObject.Instances.Clear();
        Net.Reset();
        try { test(); _passed++; Console.WriteLine("通过：" + name); }
        catch (Exception e) { _failed++; Console.WriteLine("失败：" + name + " — " + e.GetBaseException().Message); }
    }

    private static string Packet(string text, int sender = 99, bool broadcast = true)
        => JsonConvert.SerializeObject(new DataMsg(
            JsonConvert.SerializeObject(new MsgHead { module = "Chat", eventType = "Message", fromPlayer = sender, isBroadcast = broadcast }),
            JsonConvert.SerializeObject(new ChatBody { message = text })));

    private static (ServerMain, GameMain, ChatView) Setup()
    {
        var server = new ServerMain();
        GameObject.Instances[typeof(ServerMain)] = server;
        Invoke(server, "Awake");
        server.OnNetSpawnServer();
        server.OnNetSpawnClient();
        var client = new GameMain { Tests = new List<TestBase>() };
        GameObject.Instances[typeof(GameMain)] = client;
        var view = new ChatView();
        GameObject.Instances[typeof(ChatView)] = view;
        Invoke(client, "Awake");
        Net.Wire = (direction, method, args) =>
        {
            if (direction != "Observers") return;
            int previous = server.NetObject.RpcSenderClientId;
            server.NetObject.RpcSenderClientId = -1;
            Invoke(server, method, args);
            server.NetObject.RpcSenderClientId = previous;
        };
        return (server, client, view);
    }

    private static void Invoke(object target, string method, params object[] args)
    {
        MethodInfo callback = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert(callback != null, "缺少回调：" + method);
        callback.Invoke(target, args);
    }

    private static void Assert(bool success, string message)
    {
        if (!success) throw new Exception(message);
    }

    private sealed class ProbeService : ServiceBase
    {
        public int AwakeCount, DestroyCount;
        public ServerMain Server => serverMain;
        public GameMain Client => gameMain;
        public bool ServerRole => isServer;
        public override void OnAwake() => AwakeCount++;
        public override void OnDestroy() => DestroyCount++;
    }
}
