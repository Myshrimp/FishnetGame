# 投掷物同步与视觉平滑

适用版本：Unity 2022.3.62f3、FishNet 4.7.2。

## 配置

- 在非运行状态下切换 `NetworkProjectile.Use Prediction`，等待编辑器完成校验并保存预制体或场景。服务器和客户端必须使用相同配置；不支持运行中切换。
- 编辑器会同步同物体 `NetworkObject` 的预测开关、状态转发及 Rigidbody 预测类型，不使用运行时反射修改 FishNet 私有字段。
- `NetworkProjectile.Graphical Object` 指向物理根的直接子节点 `View`，渲染器放在它下面。Rigidbody 和碰撞体留在物理根，不放在 `View` 下。
- `NetworkObject.Graphical Object` 留空，不再附加另一套 `NetworkTickSmoother`、`NetworkTransform` 或自定义视觉插值。现有手雷的引用已经迁移到投掷物脚本。
- `Interpolation Ticks` 默认 2。`Adaptive Interpolation` 默认 Off，仅在预测模式中应用于观察者；RPC 始终使用固定 Tick 插值。
- 玩家手部锚点继续放在玩家的 `View` 下；玩家 `View` 应保持原有的零位移、单位旋转/缩放绑定姿态，造型偏移放在更下层节点。

## 持有与释放

持有时，投掷物停止飞行平滑、设为运动学刚体并禁用碰撞体。`LateUpdate` 只将 `View` 对齐到玩家已经平滑的手部锚点，不再移动物理根。物理根在 `OnPostTick` 更新；Unity 自动物理的 RPC 模式改在 `FixedUpdate` 更新。

物理持有位置和投掷起点通过玩家逻辑根与手部相对姿态计算，不使用玩家视觉平滑造成的世界坐标偏移。视觉持有位置仍使用实际手部世界坐标，并保留投掷物 `View` 原始局部偏移、旋转和缩放。

释放时保留当前视觉世界姿态，停止旧平滑并设置物理位置。待本帧释放位置/RPC 应用完毕后，在 `LateUpdate` 用原始局部姿态初始化新的 Tick 平滑目标，再从释放前的视觉姿态开始过渡，避免直接跳到玩家未平滑的逻辑位置，也不继续播放旧的持有历史。分离时显式保留世界缩放，兼容当前 FishNet 分离后恢复局部缩放的行为，避免根节点缩放为 7 的手雷变小。客户端停止时解除订阅并将分离的 `View` 放回物理根。

## 两种模式

### FishNet 预测

`TimeManager.Physics Mode` 必须使用 TimeManager。保留原有 Replicate、Reconcile 和状态转发；Rigidbody 插值为 None。飞行由脚本管理的 FishNet `TickSmootherController` 平滑独立 `View`，不再使用 NetworkObject 内置的旧图形平滑。

本轮仅改造表现和配置隔离，没有将持有状态、释放事件或忽略投掷者碰撞的期限改为完整的 Tick 历史状态。预测切换和近身碰撞的一致性仍需后续专项验证。

### RPC 事件与本地物理

关闭 NetworkObject 预测及状态转发。仍只广播投掷/掉落事件，各端独立模拟；这不是持续位置快照同步，不保证长时间运动或碰撞后的各端位置完全一致，也不补偿晚加入客户端的历史轨迹。

- TimeManager 物理：在 `OnTick` 施力，Rigidbody 插值为 None，使用独立 Tick 平滑。其他预测对象回放时，以 FishNet `RigidbodyPauser` 暂停该 RPC 刚体，回放结束恢复运动学状态、碰撞状态和速度；这不对 RPC 刚体执行 Reconcile。投掷 RPC 只缓存冲量，在回放结束后的实际物理步前消费一次，避免切换运动学状态清掉尚未执行的 AddForce。
- Unity 自动物理：在 `FixedUpdate` 消费投掷冲量并施力，使用 Rigidbody Interpolate，不创建 Tick 平滑器。持有期间关闭 Rigidbody 插值，释放后恢复；释放瞬间的手部视觉偏移在 `Interpolation Ticks × fixedDeltaTime` 内归零，此后仅保留 Rigidbody 插值。

不要为了某个 RPC 投掷物修改全局物理模式；同场景仍有 Rigidbody 预测对象时应继续使用 TimeManager。

## 验证

运行 `Tests/Projectiles/Run.ps1`，可通过 `-UnityPath` 指定 Unity 路径。脚本将源码、FishNet、测试和项目配置复制到 `Temp/ProjectileValidation`，使用独立 Unity 批处理运行 EditMode 测试，其中生命周期用例会进入、退出 Play Mode，不关闭现有编辑器。测试结果与日志保存在该临时目录。测试使用真实 Unity Transform/Rigidbody 和本地 FishNet 类型，但不建立客户端/服务端连接。

自动用例覆盖：

- 两种模式下逐帧手持跟随只修改视觉位置。
- 逻辑持有位置不继承玩家图形节点的平滑位移和旋转。
- 释放后恢复原始视觉局部偏移和物理状态。
- 预测开关/状态转发与所选模式一致，旧 Graphical Object 被清空。
- RPC 根据物理时钟选择插值，持有/释放时切换正确。
- RPC 刚体在其他物体回放期间暂停，并恢复速度；客户端停止时解除暂停和事件订阅。
- 两种模式连续三轮飞行/持有，保留非零视觉偏移及根节点的 7 倍缩放；停止客户端后恢复父节点。
- 在独立真实物理场景中验证“收到投掷 RPC → 回放暂停/恢复 → 下一物理步”仍获得预期速度，且冲量只应用一次。
- 移动/旋转手部后释放，Tick 平滑与 Unity 插值两种路径的首帧均保留释放前的视觉姿态。

实际画面还需在 Host 和独立客户端分别验证：持续移动/转向时持有、连续拾取/丢弃、投掷飞行及落地碰撞；再加入延迟和丢包。检查手部贴合、释放是否突跳、飞行是否均匀及断线重连后 `View` 是否恢复。日志中的 `SmoothingActive` 持有时应为 False，TimeManager 模式飞行时应为 True；RPC 的 `Prediction`、`StateForwarding` 应为 False，`RpcReplayIsolation` 在 TimeManager 模式为 True。
