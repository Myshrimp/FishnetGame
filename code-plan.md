# FaceSlapper 技术方案（基于 plan.txt 整理与改进）

## 1. 项目概述

- **游戏类型**：基于真实物理引擎的多人对战派对游戏（类猛兽派对）
- **视角**：3D 俯视角第三人称相机
- **联机方案**：底层当前使用 FishNet Pro 4.7.2，但**全部网络调用都封装在自研抽象层之后**，
  更换为 Mirror / Netcode for GameObjects 只需修改一行代码（见第 5 节）。
- **初版范围（里程碑 M1）**：局域网房间（Host/Join）→ 开始游戏 → 玩家生成 → 移动 → 拾取武器 → 攻击 → 击飞。除 GM 调试命令行外不接任何 UI。

## 2. 技术栈与项目现状

| 项 | 版本/说明 |
|---|---|
| Unity | 2022.3.62f3 |
| 网络 | 自研抽象层 `FaceSlapper.Networking`，当前后端 FishNet Pro 4.7.2（Tugboat UDP） |
| 渲染 | URP 14 |
| 输入 | 旧版 Input Manager（`activeInputHandler: 0`），代码全部用 `KeyCode` 轮询，不依赖 InputManager.asset 轴配置 |
| 场景 | 初版单场景 `Main.unity`（大厅+竞技场一体） |

## 3. 总体架构

```
GameEntry（场景引导器，Awake 时创建一切）
 └─ GameManager（全局唯一单例 MonoBehaviour，跨场景不销毁）
     ├─ 组件注册表 Dictionary<Type, IGameComponent>，Get<T>() 访问任意组件
     ├─ InputComponent ── InputHandler（纯 C# 轮询）
     ├─ EventBus / PoolComponent / TimeComponent / LogComponent
     ├─ SceneManagementComponent ── SceneHandler
     ├─ GMComponent + GMConsole（IMGUI 命令行）
     ├─ NetworkComponent ──┐
     ├─ TickComponent ─────┤
     └─ RoomHandler ───────┤
                           ▼
              ┌─────────────────────────────┐
              │  Net 门面（唯一网络入口）      │  ★ 游戏代码只允许调用这里
              │  Net.Server/Client/Events    │
              └─────────────┬───────────────┘
                            │ INetBackend
              ┌─────────────▼───────────────┐
              │  FishNetBackend（当前实现）   │  全部 FishNet 引用收口于此 +
              │  FishNetNetObjectBridge      │  FishNetNetObjectBridge 两个文件
              └─────────────────────────────┘

游戏对象上的网络组件（全部后端无关）：
 ├─ NetObject（身份/所有权/生命周期事件/RPC 与 NetVar 路由）
 ├─ NetBehaviour（需要 RPC/NetVar 的逻辑基类）
 ├─ NetTransformSync（位置旋转同步，20Hz 不可靠通道 + 接收端插值）
 └─ [后端附加] FishNet NetworkObject + FishNetNetObjectBridge（由搭建工具自动添加）
```

## 4. 目录结构（全部位于 `Assets/Scripts/`）

```
Assets/Scripts/
├─ Core/            GameManager, GameEntry, IGameComponent, Singleton, EventBus, Events,
│                   Observer(Subscriber/Notifier), Pool/, Log/, Time/, Scene/, GM/
├─ Input/           InputHandler, InputComponent, InputSnapshot
├─ Networking/      ★ 联机抽象层（后端无关）
│   ├─ Net.cs              静态门面（含 ★一行切换后端的工厂）
│   ├─ INetBackend.cs      后端接口（连接/生成/查找/所有权/事件/场景/编辑器钩子）
│   ├─ NetObject.cs        网络对象标识组件 + RPC/NetVar 路由
│   ├─ NetBehaviour.cs     网络逻辑基类（SendServerRpc/ObserversRpc/TargetRpc）
│   ├─ NetRpcAttribute.cs  [NetRpc] 标记 + 反射派发（带类型缓存）
│   ├─ NetVar.cs           NetVar<T> / NetList<T>（服务器权威同步变量/列表）
│   ├─ NetSerializer.cs    二进制序列化（基元/Vector/枚举/INetSerializable）
│   ├─ NetTransformSync.cs 位置旋转同步（替代 NetworkTransform）
│   └─ FishNetImpl/        ★ 当前后端（全项目仅此处引用 FishNet）
│       ├─ FishNetBackend.cs          INetBackend 实现 + 事件绑定 Runner
│       └─ FishNetNetObjectBridge.cs  NetworkBehaviour 桥接（RPC/NetVar/Transform 通道）
├─ Network/         NetworkComponent（Host/Join/Stop 封装）、TickComponent
├─ Room/            RoomComponent, RoomHandler, RoomPlayerInfo
├─ Match/           MatchComponent（对局管理器，服务器权威状态机）, MatchHandler,
│                   MatchTypes(MatchState/MatchPlayerInfo), GameMode(GameModeBase + 模式注册表)
├─ Camera/          TopDownCamera
├─ Battle/          NetworkIdentity, Movement, PlayerSpawnPoints,
│                   Abilities/(IAbility, AbilityComponent, Hitback, PickWeapon, SpeedUp),
│                   Buff/(IBuff, BuffBase, BuffComponent, PowerBuff)
├─ Weapon/          IWeapon, WeaponBase, SlapperWeapon
└─ Editor/          FaceSlapperSetup（后端感知的一键搭建）
```

## 5. 联机抽象层设计（后端可替换）

### 5.1 更换后端的方法（一行代码）

```csharp
// Assets/Scripts/Networking/Net.cs
// ★★★ 切换联机后端只需修改这一行 ★★★
private static INetBackend CreateBackend() => new FishNetImpl.FishNetBackend();
// private static INetBackend CreateBackend() => new MirrorImpl.MirrorBackend();   // Mirror
// private static INetBackend CreateBackend() => new NetcodeImpl.NetcodeBackend(); // Netcode for GameObjects
```

新后端需要实现：
1. `INetBackend`（约 20 个方法/事件，直接映射到底层库 API）；
2. 一个挂在网络对象上的桥接组件（实现 `INetObjectBridge`：三种 RPC 路由及其通道参数 + NetVar 转发 + Transform 转发 + 生命周期转发）；
3. 编辑器钩子：给 Prefab 添加底层库组件、注册可生成对象、放置网络管理器。

之后运行一次 `FaceSlapper/Setup All` 重建 Prefab/场景即可。

### 5.2 抽象层的关键决策

| 决策 | 说明 |
|---|---|
| 自研 NetVar/NetList | 用可靠 RPC 通道传输（值序列化为字节流），不依赖各库的 SyncVar 代码生成，三个后端写法完全一致；客户端 OnStartClient 时向服务器拉全量状态，解决迟加入/竞态 |
| 自研 RPC 派发 | `[NetRpc]` 标记 + 方法名 + 类型标签参数序列化 + 反射缓存派发；语义三件套：SendServerRpc / SendObserversRpc / SendTargetRpc(clientId)，默认可靠，通道重载支持 `NetChannel.Unreliable` |
| 自研 NetTransformSync | 替代 NetworkTransform：控制端（Owner 或无主时的服务器）20Hz 不可靠广播，接收端 100ms 延迟插值；接收端刚体运动学，杜绝双重物理模拟 |
| 身份用 int NetId | 对象查找 `Net.Server/Client.TryGetObject(netId)`，不暴露底层对象类型 |
| 所有权抽象 | `NetObject.IsOwner/IsController/OwnerClientId` + `Net.Server.Give/RemoveOwnership` |

### RPC 通道选择

底层使用 FishNet Tugboat（LiteNetLib），同时支持可靠有序传输和不可靠 UDP，无需引入 KCP。`NetChannel` 位于后端无关的 Networking 层，三种 RPC 都可以显式选择通道；原有调用默认保持可靠。

```csharp
SendServerRpc(nameof(CmdInput), tick, moveX, moveY); // 默认可靠
SendServerRpc(NetChannel.Unreliable, nameof(CmdInput), tick, moveX, moveY);
SendObserversRpc(NetChannel.Unreliable, nameof(RpcSnapshot), tick, position);
SendTargetRpc(NetChannel.Unreliable, clientId, nameof(RpcSnapshot), tick, position);
```

上述为调用形式示例，方法名和参数应替换为实际 `[NetRpc]` 方法。显式通道放在第一个参数，避免与原有 `params object[]` 中的业务枚举或零值混淆。通道从 `NetBehaviour` 经 `NetObject`、`INetObjectBridge` 传入 FishNet，不写入业务参数。服务端（含 Host）调用 ServerRpc 仍直接本地派发。

不可靠通道不保证到达和顺序，适用于允许丢失且由后续消息覆盖的快照；需要顺序的接收方应使用业务 tick/序号丢弃旧数据。现有帧同步输入、聊天和业务 RPC 不自动切换通道；NetVar、全量状态补发仍可靠，Transform 同步仍不可靠。FishNet 会把超过不可靠 MTU 的 RPC 自动改走可靠通道，因此应控制快照大小。

回归验证：安装 .NET 10 SDK 后，在项目目录执行 `powershell -NoProfile -ExecutionPolicy Bypass -File Tests/Networking/Run.ps1`。测试直接编译网络封装与序列化源码，仅替代 Unity 原生组件环境、全局服务器状态和底层桥接边界，覆盖三种路由的默认/显式通道、参数兼容、空参数、服务器发送限制与接收派发。该测试不替代 Unity 编译、FishNet RPC 代码生成及双端网络联调；联调时应分别验证客户端→服务器、服务器→观察者/指定客户端的小包，并在丢包/乱序条件下检查业务容错。

### 5.3 网络同步模型

| 数据 | 权威方 | 手段 |
|---|---|---|
| 房间状态/玩家列表/开始游戏/生成销毁 | **服务器** | NetVar/NetList + ServerRpc |
| 玩家移动/旋转/受击飞位移 | **客户端 Owner** | 本地 Rigidbody 模拟 + NetTransformSync 广播 |
| 命中判定与击飞校验 | 攻击方检测 → **服务器校验** → 受害者 Owner 执行 | ServerRpc → 距离校验 → TargetRpc |
| 武器归属/拾取 | **服务器** | ServerRpc 申请 → NetVar HolderNobId + 所有权转移 |

升级路径：后续可迁移到服务器权威物理（客户端预测 + 和解），`TickComponent` 已预留网络 Tick 挂载点。

## 房间公共聊天（Separated / SyncTest）

`Assets/Scenes/SyncTest.unity` 的聊天面板已绑定消息预制体、输入框、发送按钮与滚动列表。运行场景后按 Q 启动 Host，另一客户端按 R 连接 `ServerMain.IP`（默认本机）；输入文字后点击发送或按回车。输入框选中时不响应 Q/R/空格测试快捷键。离线、连接尚未分配 ID、客户端聊天对象尚未生成或校验失败时保留输入，并在面板显示原因。

聊天通过默认可靠 RPC 传输，与帧同步输入流独立。客户端 `GameMain.Chat` 发起 `DataMsg`，服务器验证后向当前网络对象的观察者广播，客户端仅在收到服务器回显后显示一次。当前只支持公共聊天，不支持私聊和历史补发。

- 外层：`DataMsg.head` / `DataMsg.body` 均为 JSON 字符串，外层 JSON 最长 8192 个字符。
- 消息头统一使用 `MsgHead`：`module = "Chat"`、`eventType = "Message"`、`isBroadcast = true`；服务器用 RPC 连接的真实 ClientId 重建 `fromPlayer`，Host 本地发送使用本机连接 ID；发送者必须是当前连接。
- 消息体使用 `ChatBody.message`：去除首尾空白后必须为 1～256 个 UTF-16 字符单位，空白、超长、错误 JSON 和非公共消息均不广播。
- 服务端 `ChatService` 在 `ServerMain.OnNetSpawnServer` 注册，在服务器停止或对象销毁时释放；客户端服务由 `GameMain` 持有并随其销毁释放。服务端请求使用 `ServerChatEvent`，客户端展示使用 `ChatEvent`，避免 Host 重复处理及再次广播。
- UI 以纯文本显示 `[User ClientId]:内容`，关闭富文本解析，自动换行并滚动到最新消息，只保留最近 100 条；`ChatTextItem` 支持实例化后立即设置文字。

代码调用示例：`gameMain.Chat.TrySendMessage("你好", out string error)`，返回值表示本地校验通过并发起发送，不代表服务器已确认投递。定制服务列表应包含 `ChatService`；未配置或为空时默认注册它。

回归测试：Unity 已恢复项目依赖且安装 .NET 10 SDK 后，执行 `powershell -NoProfile -ExecutionPolicy Bypass -File Assets/Scripts/FrameSync/Separated/Tests~/Run.ps1`（复用原聊天用例并扩充边界替身）。测试编译实际服务、事件总线、JSON 协议和 RPC 入口源码，以边界替身验证 Host/远端发送者、非法请求、一次回显、离线发送及服务重启清理；真实 UI、RPC 代码生成和网络传输仍需 Unity 联调。本次验证已通过 12 项聊天回归、18 项网络通道回归、Unity 2022.3.62f3 构建（含 FishNet 代码生成），以及独立 Host/客户端的按钮收发、服务器身份重建、非法消息拒绝、一次回显、长文本布局、100 条列表上限和离线保留输入检查；界面已完成渲染检查。

## 6. 其余模块设计要点

- **GameManager/GameEntry**：组件注册表 + 统一生命周期分发（IUpdatable/IFixedUpdatable/ILateUpdatable）。
- **EventBus**：泛型 struct 事件，零装箱；典型事件：LocalPlayerSpawnedEvent、RoomStateChangedEvent、PlayerHitEvent。
- **Input**：InputHandler（纯 C# KeyCode 轮询）→ InputSnapshot；InputComponent 每帧派发；GM 命令行聚焦时输出空快照。
- **Camera**：TopDownCamera 固定偏航俯视角、滚轮缩放（联动俯仰角）、SmoothDamp 跟随本地玩家。
- **武器"虚拟挂载"**：不用网络父子同步；持有者 Owner 每帧对齐手部挂点，其他端插值。
- **GM 命令行**：`` ` `` 呼出，`/gm func MethodName(arg1,arg2)` 反射调用 GMComponent（参数支持 int/float/bool/string）。内置：Host / Join(ip) / Stop / StartGame / SpawnWeapon(x,z) / ListPlayers / SetTeam / Log。
- **按键**：WASD 移动、鼠标左键攻击、E 拾取/放下、Q 耳光、Shift 加速、滚轮缩放。

## 7. 编辑器一键搭建

`FaceSlapper/Setup All`（后端感知，换后端无需改它）：
1. 生成 `Player.prefab` / `SlapperWeapon.prefab`（物理组件 + 游戏脚本 + NetObject + NetTransformSync + 后端组件）；
2. 注册可生成对象（后端钩子，当前写入 DefaultPrefabObjects.asset，并自动清理失效条目）；
3. 创建 `Main.unity`：网络管理器（后端钩子）+ GameEntry + Room（场景网络对象）+ 竞技场 + 4 出生点 + 相机 + 灯光，加入 Build Settings。

## 8. 美术资产（程序化生成）

主角形象：球形体（半径 0.6）+ 米白底黑豆豆眼 + 两侧低模卡通手掌。全部资产由 `FaceSlapper/Generate Art Assets`（也被 Setup All 调用）程序化生成，无需外部建模工具：

| 资产 | 说明 |
|---|---|
| `Assets/Art/Shaders/ToonLit.shader` | URP 卡通 Shader：Ramp 分阶光照（含主光源阴影衰减）+ 反向壳描边 + 阴影投射 |
| `Assets/Art/Shaders/ToonDecal.shader` | 通用贴花 Shader：**形状由贴图 Alpha 决定、与面片形状无关**（任意 Quad 可用）。无光照、透明混合、不写深度 + 深度偏移防 Z-Fighting；支持硬边裁剪（默认）/柔边混合两种模式、剔除模式可调 |
| `Textures/EyeDotTex.png` + `Materials/dot_eye.mat` | 透明底黑椭圆贴图（羽化边缘，Clamp）+ 贴花材质；眼睛用普通 Quad 贴在球面上（法线沿球心朝外） |
| `Textures/FaceDots.png` | 256×256 脸部贴图：纯色米白底 + 两个黑色竖椭圆豆豆眼 |
| `Textures/ToonRamp.png` | 128×8 三阶硬边 Ramp（暗 0.3 / 中 0.6 / 亮 1.0，Point 采样） |
| `Meshes/HandRelaxed_R/L.asset` 等 | 低模手掌网格：掌心 + 四指两节 + 拇指两节（复用内置立方体拓扑拼装），放松/握持两种弯曲参数，右手镜像生成左手 |
| `Materials/M_ToonBody/Face/Hand/Weapon.mat` | ToonLit 材质实例 |

玩家 Prefab 结构：SphereCollider + Body（球） + EyeL/EyeR（贴花豆豆眼） + Hands/HandR|HandL（Relaxed+Grip 两套网格） + HandSocket（右手掌心）。`HandVisuals` 脚本在持有武器时自动把右手从放松切换为握持；`NetworkIdentity` 按玩家序号给身体与双手染色。

## 9. 实现与验证状态

- 全部脚本已实现；在临时工程批处理验证：**编译 0 错误（含 FishNet IL 织入检查）**；
- `SetupAll` 已在批处理模式实际执行成功，产物（Prefabs/Materials/Main.unity/DefaultPrefabObjects/EditorBuildSettings + 全部 .meta）已回拷到本工程；
- 注意：NetVar 采用"客户端生成时拉全量"模型，服务器在对象生成瞬间的变量广播若与生成消息竞态，可能产生少量无害的时序日志，随后由全量拉取收敛。

## 9. 后续扩展路线

1. 实现 MirrorImpl / NetcodeImpl 后端（改 Net.cs 一行 + Setup All 一次）；
2. 服务器权威物理（客户端预测 + 和解）替换客户端权威移动；
3. Lobby/Game 场景拆分（SceneManagementComponent 已就绪）；
4. 互联网联机（NAT 穿透或 Relay）；
5. 队伍/积分/回合玩法、UI 接入（EventBus 已就绪）；
6. 命中特效/音效走 PoolComponent + ObserversRpc。

## SyncTest 隐患修复

后续脚本开发限定在 `Assets/Scripts/FrameSync/Separated/`。回归入口为 `powershell -NoProfile -ExecutionPolicy Bypass -File Assets/Scripts/FrameSync/Separated/Tests~/Run.ps1`，沿用聊天用例并补充房间回归；`Tests~` 不进入 Unity 导入。

1. 状态和身份接收 RPC 仅接受服务器下行，拒绝远端 ServerRpc 冒充；状态 JSON 上限 65536 字符，玩家 ID 非负且不重复，身份必须匹配本地连接。异常消息不触发业务回调。
2. 玩家模拟状态以 ClientId 为键，加入时保留已有状态并设置在线标记；断线移除身份与模拟状态，服务器停止时清空会话。
3. HUD 按最新玩家快照更新列表，重复快照不累积 ID，离开者的 UI 项被移除，空快照清空列表。
4. Q/R 连接请求在发起失败和停止事件后恢复可重试状态，连接中阻止重复发起；客户端断线清除本地身份并发布空房间快照。
5. 客户端对象与连接身份就绪后自动申请初始化；服务端按真实请求者定向返回身份和快照，加入/退出各广播一次变化。空格仅用于手动刷新，不再是初始化前提。
6. SyncTest 将 GameMain 移至独立 SceneGame 场景对象，常驻 GameManager/NetworkManager 与业务生命周期分离；重载场景重建业务服务及绑定。编辑器批处理入口 SyncTestSceneValidation.Validate 检查场景引用和常驻根隔离。
7. HUD 使用具名事件处理函数，在 OnEnable/OnDisable 成对注册与解绑，避免已停用或销毁的界面继续响应全局事件。
8. 聊天按真实连接使用令牌桶限频（最多突发 3 条，每秒恢复 2 条）；客户端限频时保留输入并提示，服务端独立校验，断线/服务释放清理额度。UI 每帧批量追加并合并布局刷新，待显示队列与列表各保留最近 100 条。

本轮验证：12 项原聊天用例 + 12 项房间修复用例通过；Unity 场景隔离和批量聊天校验通过；Unity 2022.3.62f3 构建及独立 Host/客户端联调通过，覆盖自动初始化、防伪造、限频、长文本、100 条上限、退出、重连、场景重载与重载后的聊天。双端可重复验证命令：powershell -NoProfile -ExecutionPolicy Bypass -File Assets/Scripts/FrameSync/Separated/Tests~/RunUnity.ps1。

## Separated 定点质点运动模型

入口：`FaceSlapper.FrameSync.Separated.Physics.PointMassMotion.Step`。复用现有 `FP`（精度 1/4096）与 `FPVec3`，计算不依赖 Unity、浮点数、渲染帧率或全局状态。

输入为位置（m）、正质量（kg）、当前速度（m/s）、外力总和（N）和逻辑帧时长（s），返回下一逻辑帧位置。外力不包含本模型内部计算的空气阻力；重力等需要调用方自行合入外力。本模型只计算自由质点运动，不包含碰撞或地面约束，也不自动驱动场景角色。

空气阻力为线性阻力 `F_drag = -k * v`，默认 `k = 20 kg/s`，可通过带 `airResistance` 参数的重载调整，设为 0 可关闭。每个逻辑帧内外力、质量、阻力系数视为恒定，采用后向欧拉近似：

```text
v_next = (mass * velocity + totalForce * deltaTime) / (mass + k * deltaTime)
p_next = position + v_next * deltaTime
```

这是一步一阶数值积分，不是连续运动的解析解；即使关闭阻力，也使用新速度更新位置。阻力自身不会使速度反向；足够大的反向外力仍然允许真实反向运动。各端必须传入相同的定点帧时长，较大的帧时长虽然稳定，但轨迹近似误差更大。

```csharp
FP dt = FP.FromRaw(FP.ONE / 30);
FPVec3 nextPosition = PointMassMotion.Step(position, mass, velocity, totalForce, dt);

// 连续模拟应同时保存新速度。
nextPosition = PointMassMotion.Step(position, mass, velocity, totalForce, dt, out FPVec3 nextVelocity);
position = nextPosition;
velocity = nextVelocity;

// 自定义阻力系数，其他参数顺序保持一致。
nextPosition = PointMassMotion.Step(position, mass, velocity, totalForce, dt, FP.FromInt(10), out nextVelocity);
```

质量必须大于 0，帧时长和阻力系数必须非负，否则抛出 `ArgumentOutOfRangeException`；零时长原样返回位置和速度。为避免小质量时动量提前截断，速度分量使用 Raw 整数乘加后统一除以定点分母，最后量化为 FP；位移沿用 FP 乘法的有符号右移规则。所有整数中间乘加启用溢出检查，超出 Int64 中间值范围抛出 `OverflowException`，不静默回绕或饱和；调用方需要保证输入量级合理，不能将整个 FP 表示范围视为可安全相乘的范围。

测试：`powershell -NoProfile -ExecutionPolicy Bypass -File Assets/Scripts/FrameSync/Separated/Tests~/RunPhysics.ps1`。需要 .NET 10 SDK，直接编译真实 FixedPoint 和质点模型，不使用数学替身。17 项用例覆盖静止、三轴力、惯性、质量响应、阻力平衡、反向外力、大步长稳定性、微小质量和冲量、连续帧、非法参数、溢出及重复模拟一致性。

## Separated 定点弹簧受力模型

入口为 `PointMassForces.Calculate(targetIndex, points, springs)`，返回目标质点当前帧的合力（N），可直接作为 `PointMassMotion.Step` 的 `totalForce` 参数。所有脚本位于 `Separated/Physics/`，使用现有 `FP/FPVec3`，不依赖 Unity 或浮点数，不修改输入快照。

- `PointForceState(Position, ExternalForce)` 保存质点位置（m）和自身外力总和（N）；不需要质量或速度，因为本次只求当前形变产生的弹力。
- `SpringConnection(PointA, PointB, RestLength, Stiffness)` 使用质点列表中的索引标识两端；原长（m）和劲度系数（N/m）均须非负，两端索引必须不同且在列表内。原长或劲度系数允许为零。
- `PointMassForces.SpringForce(targetPosition, otherPosition, restLength, stiffness)` 可单独计算一条弹簧对目标端的作用力。

```text
d = otherPosition - targetPosition
L = |d|
F_spring = stiffness * (L - restLength) * d / L
F_target = target.ExternalForce + sum(与目标相连的 F_spring)
```

支持拉伸产生吸引力、压缩产生排斥力；两端完全重合时方向不唯一，约定该弹簧返回零力，调用方应避免以重合位置初始化需要分离的质点。相同端点可有多条弹簧，按独立并联弹簧叠加；与目标不相连的弹簧不贡献力。其他质点的外力不会直接叠加到目标，它们先驱动各自运动，再通过后续弹簧形变影响目标；本接口不求静力平衡或即时外力传递。

```csharp
var points = new[]
{
    new PointForceState(FPVec3.Zero, FPVec3.Zero),
    new PointForceState(new FPVec3(FP.FromInt(3), FP.Zero, FP.Zero), FPVec3.Zero)
};
var springs = new[] { new SpringConnection(0, 1, FP.One, FP.FromInt(10)) };
FPVec3 force = PointMassForces.Calculate(0, points, springs); // (20, 0, 0) N
position = PointMassMotion.Step(points[0].Position, mass, velocity, force, deltaTime, out velocity);
```

逐帧更新时，先用同一帧的位置快照算出所有质点的力，再统一积分和替换快照。不要在计算其他质点受力前改写位置，也不要将上帧计算出的弹簧合力填入下一帧的 `ExternalForce`，以免重复累积。重力或输入力由调用方加入自身外力；空气阻力由已有运动模型计算，不能重复加入。当前弹簧不包含速度阻尼、断裂、碰撞或刚性约束。运动模型的后向欧拉只隐式处理空气阻力，弹簧仍按帧初位置显式求力；高刚度、小质量或大步长可能不稳定，应减小统一逻辑步长或对整个系统同步子步进。

距离直接对 Raw 平方和取整数平方根（向下取整），保留微小距离；弹力乘除向零截断，交换两端时保证逐位等大反向。所有距离、受力与累加中间运算检查 Int64 溢出，超出范围抛 `OverflowException`；输入规模须保证中间平方、乘加可表示，并非整个 FP 范围都能计算。空引用、无效目标索引、无效端点及负弹簧参数明确抛出参数异常。单个目标计算遍历弹簧列表，时间 O(弹簧数)，计算中不分配临时集合。

验证沿用 `Separated/Tests~/RunPhysics.ps1`：17 项运动测试及 16 项受力测试通过；新增覆盖拉伸、压缩、三维方向、微小间距、两端反力、重合点、自身外力、多弹簧叠加、其他外力隔离、内力守恒、运动模型联动、非法参数与溢出。
