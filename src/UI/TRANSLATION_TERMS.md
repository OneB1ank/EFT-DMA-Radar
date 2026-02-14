# XAML UI 汉化术语与文案风格规范

适用范围：`src/UI` 下所有 XAML 可见文案（`Content`、`Text`、`Header`、`ToolTip`、`StringFormat`）。

## 1) 全局术语映射（统一写法）

| 原文/混写 | 统一写法 |
| --- | --- |
| Local Player | 本地玩家 |
| Aimline | 瞄准线 |
| Ammo | 弹药 |
| Silent Aim / Silentaimbot | 静默自瞄 |
| Device Aimbot / DeviceAimbot | 设备自瞄 |
| Memory Writes | 内存写入 |
| Master Switch | 总开关 |
| Bone | 骨骼 |
| Recoil | 后坐力 |
| Sway | 晃动 |
| Infinite Stamina | 无限体力 |
| Anti-AFK / AFK Timer | 防挂机 / 挂机计时器 |
| Debug | 调试 |
| Debug Console | 调试控制台 |
| Debug Overlay | 调试叠加层 |
| InputManager | 输入管理器 |
| Memory Inspector | 内存查看器 |
| Search structs or fields | 搜索结构体或字段 |
| Base | 基址 |
| Read | 读取 |
| Auto / Auto-refresh | 自动 / 自动刷新 |
| Select struct | 选择结构体 |
| Back | 返回 |
| Field / Offset / Value | 字段 / 偏移 / 数值 |
| ESP Fuser | ESP 融合器 |
| Preview | 预览 |
| Crosshair | 准星 |
| FOV Circle | FOV 圆 |
| Local Player Aimline | 本地玩家瞄准线 |
| Local Player Ammo | 本地玩家弹药 |
| PMC | PMC（保留） |
| Player Scav | 玩家拾荒者 |
| AI Scav | AI 拾荒者 |
| Raider | 掠夺者 |
| Boss | 首领 |
| BEAR / USEC | BEAR / USEC（保留） |
| FPS | FPS（保留） |
| CPU/GPU | CPU/GPU（保留） |
| px | px（保留） |
| x（倍数） | x（保留） |

## 2) 文案风格规则

- 尽量全中文；专有缩写保留：`ESP`、`FOV`、`FPS`、`PMC`、`BEAR`、`USEC`、`CPU/GPU`、`px`。
- 不混用同义词：`Boss` 统一为 `首领`；`Scav` 统一为 `拾荒者`；`Aimline` 统一为 `瞄准线`。
- 优先名词短语，避免口语化：如“显示手榴弹轨迹”优于“显示手雷路径”。
- ToolTip 统一使用说明句式：`用于...`、`启用后...`、`在...时...`，避免英文祈使句直译。
- 标点统一中文全角：`：`、`（ ）`；仅在代码样式或缩写参数中保留半角符号。
- 数值格式统一：
  - 距离：`{0:F0} m`
  - 像素：`{0} px`
  - 比例/倍数：`{0:F1}x`
  - 百分比：`{0:F0}%`
- `StringFormat` 只改提示文字，不改占位符和绑定表达式。

## 3) 易错项（优先修正）

- `AI瞄准线` -> `AI 瞄准线`（中英文之间补空格）。
- `锁定 玩家 Scav` -> `锁定玩家拾荒者`。
- `锁定 AI Scav` -> `锁定 AI 拾荒者`。
- `Local Player Aimline` -> `本地玩家瞄准线`。
- `Local Player Ammo` -> `本地玩家弹药`。
- `Enable Memory Writes (Master Switch)` -> `启用内存写入（总开关）`。
- `WARNING: High Detection Risk` -> `警告：检测风险高`。

## 4) 执行约束

- 不改 `Binding Path`、命令、控件名、资源键、`x:Name`。
- 仅替换可见文案；运行时由 `.cs` 生成的英文字符串另行登记处理。
