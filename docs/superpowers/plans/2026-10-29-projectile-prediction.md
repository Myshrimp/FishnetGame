# 投掷物预测一致性实施计划

**目标：** 修正飞行与碰撞后的可重复预测分歧，不通过延长视觉插值掩盖物理错误。

**设计依据：** 用户已确认本轮补齐预测状态、统一释放与碰撞时序、增加诊断及测试；保留服务器批准投掷，不改变控制权、玩法和 RPC 同步方案。

**技术栈：** Unity 2022.3.62f3，项目内 FishNet 4.7.2。

## 边界与接口

- 修改 `Assets/Scripts/Network/NetworkProjectile.cs`，不修改 FishNet。
- 增加 `ProjectileMotionState` 和 `ProjectilePredictionState`，后者记录运动状态、持有者、投掷者、转换序号和剩余忽略碰撞步数。
- `ProjectileReplicateData` 携带服务器确认的转换及姿态；`ProjectileReconcileData` 携带完整预测状态。双方须使用同版程序。
- `BuildPredictionData()` 在服务器 Tick 生成命令；`PerformReplicate` 应用命令、更新碰撞规则并施力；`PerformReconcile` 恢复物理和业务状态。
- 忽略期限使用可回滚的剩余模拟步数，避免跨机器比较不同 Tick 时钟。
- 物理状态与表现状态分离。SyncVar 不直接修改预测刚体，历史回放不反复启停图形平滑。
- 服务器仍是唯一命中结算端；同一转换序号不重复重置命中标记。
- 保留现有文件编码和换行风格，不提交、不修改无关工作区文件。

## 任务

### 1. 可回滚状态与原子释放

- [x] 新增 `Assets/Tests/Editor/NetworkProjectilePredictionTests.cs`，先验证旧实现的持有状态污染、释放回放及重复冲量问题。
- [x] 运行 `Tests/Projectiles/Run.ps1 -Label prediction_red_clean`，确认新增六项用例因行为缺口失败，既有 16 项通过。
- [x] 实现状态结构、Tick 命令、序号去重和 Reconcile 恢复；只有 Created 输入应用事件，未来推算仅继续物理。
- [x] 复跑测试 `prediction_state_green2`：22/22 通过，验证释放前回滚能再次施加冲量，旧消息不会改变新状态。

### 2. 碰撞规则与生命周期

- [x] 验证客户端设置忽略碰撞、期限到期、回滚恢复、换投掷者和停止网络时清理。
- [x] 将预测碰撞忽略与 RPC 的实时时钟逻辑隔离。
- [x] 在独立 PhysicsScene 验证静态反弹重放，以及移动障碍按相同历史恢复后的碰撞重放。

### 3. 诊断与验收

- [x] 添加同 Tick 历史误差与整轮重放后的当前位置变化指标，使用有界历史，仅在诊断启用时记录。
- [x] 更新 `docs/projectile-synchronization.md`，说明序号、期限、回放与实际联网验收限制。
- [x] 运行全部投掷物用例及现有项目测试入口，记录阻塞；检查编译日志、编码、diff 和旧描述。
- [x] 独立审查本轮修改，针对重要问题增加回归用例并修复。

## 重点验证

- SyncVar 先到或后到，不改变历史模拟的运动状态。
- 重复、过期和未来输入不重复释放；回滚前后的合法重放不被永久去重。
- 忽略碰撞包含释放步，截止后下一步恢复；回滚跨过截止点时规则恢复。
- 重新拾取、换投掷者、断开和池化不残留忽略碰撞或待执行释放。
- 静态反弹与移动障碍测试证明可控历史下的重放，不冒充真实联网画面验收。

## 执行记录

- 初始工作区只有既有子模块脏标记。本轮直接在用户当前工作区执行，不创建分支或提交。
- 既有目标脚本 UTF-8 无 BOM、6 处 CRLF，其余 LF；新增行使用 LF。测试和说明为 UTF-8 无 BOM、LF。
- FishNet 自动比较器不能递归比较 NetworkObject 成员，使用 CustomComparer 比较状态字段与对象引用；未改 FishNet 源码。
- `prediction_collision_red` 为 27/28，仅新增诊断用例失败；补上诊断并修正测试设置 Transform 的方式后，`prediction_release_red` 为 28/29，仅新增释放连续性用例失败。已改为保留上一渲染帧的手部姿态，等待完整复验。
- Chat/Networking 入口均因引用已删除的 FrameSync/Separated 和 Networking 源码而 CS2001；未改这些旧测试路径。
- 运行期间出现 ProjectSettings/ProjectSettings.asset 的 preloadedAssets 修改，不属于本轮编辑，保留不回滚。
- `prediction_final_review`：29/29 通过，包含原有 16 项、12 项预测行为及新增释放视觉回归；编译日志未发现脚本错误/异常。目标文件 `git diff --check` 通过。
- 当前脚本仍为混合换行，保留 4 处 CRLF，新增行 LF；未做整文件换行转换。
- 独立审查未确认 P0/P1；指出释放测试绕过 Spawn/预测状态分支，已改为设置 Spawn 状态并经 Replicate 输入后由 LateUpdate 切换，新增位置、旋转及平滑启用断言。
- 审查提出 CharacterController 重启可能清除 IgnoreCollision 的疑点；在当前 Unity 版本中实际重启并模拟接触后，碰撞忽略和速度均保持，无需修改缓存逻辑。
- 已补投掷者入队后销毁的用例。决策：不增加当前未使用的玩家池化身份协议；未来启用池化前须补 Spawn 身份校验，否则可能采样复用对象的新姿态。该限制已写入现行文档。
- 最终 `prediction_review_edges`：31/31 通过。测试使用真实物理及反射预测入口，未建立 Host/独立客户端连接，不能替代实机网络验收。
