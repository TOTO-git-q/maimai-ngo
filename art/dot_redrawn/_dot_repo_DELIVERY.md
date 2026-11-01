# 皮肤素材审阅入口

这是 **GitHub 素材候选审阅交付**，不是游戏安装包，也不表示整套游戏素材已完成或通过验收。

## 当前应审阅哪一版

**本 overlay 仅为其中明确列出的 identity 提供增量选择与状态，不是全套皮肤 manifest。未列出的 identity 保留各自历史状态与既有选择，不因本交付自动合格，也不意味着整套皮肤只有这 25 项。**

以 [`current_selection_overlay.json`](current_selection_overlay.json) 的 `current_selected` 为准：每条都列出原始 identity、source blob、当前 PNG、配套 metadata、提交、复核证据与历史记录。**不要根据目录日期猜测，也不要把旧 registry 当成本轮最新选择。**

`registry/redraw_status.json` 原样保留，作为历史。所有旧文件、失败稿和旧 INDEX 均保留；`nonselected_dispositions` 仅供诊断，不是替换素材。`pending_selected` 未经发布核验，不覆盖当前选择。

## 已核验范围

- 当前已核验素材提交：`62269afb63c2c9ccb0d34baaaba29b16d6b02b90`；tree `24165182a344d13edd48aab6507ff73a18c01630`，6,993 个叶文件。
- 本轮已发布选择按 **identity + source blob** 去重：**25 项 = 18 项新原生修订候选 + 3 项仅几何调整候选 + 4 项已有 fitted 原字节选择**。不是 25 项 owner/游戏内验收。
- 其中 **4 项已有 fitted 原字节选择已发布核验**；没有新增绘制，也没有新增几何操作，不计入 18 项新原生修订。
- 本轮六份索引共 **60 条、60 个唯一 identity/source 配对**：25 个已发布选择、35 个非选择诊断记录。MusicLock 六项第二轮已发布归档，全部 hold，新增选择为 0；合成明暗底诊断不是运行时验证。
- 11 项第二轮几何 hold 的限定复查已完成：**0 项可在允许的保留约束内修复，全部维持 hold**；没有新原生调用或新衍生候选，也不是工具拒绝。
- 已登记库存为 **1,055 个 identity/source 配对、1,040 个不同 source blob**。当前 994 个配对有输出绑定，但包含历史复用、衍生、源图保留及不一致的 QA，**不能解读为 994 张合格新画**。
- 固定 309 范围仍为：273 原生候选、3 几何候选、1 精确复用、28 attempted hold/failed、4 未尝试技术门槛。该分区不等于全游戏库存。历史 931/15/49/37 为重叠口径，禁止相加作总数或百分比。

## 仍未跨过的门槛

1. **源/消费端契约**：Nationaldata S、SS、SSS、SSSp 四项须确认 serialized `Image.type`、`RectTransform` 宽度、父级 sliced/stretch 约定及合格家族 master；是技术未决，不是生成失败。Nationaldata_Base 是不同 identity。
2. **离线 hold**：保留字形、透明度、内边界、布局和状态配准等实际缺陷。已知本阶段“两次已完成原生尝试”的记录只表示本阶段尝试耗尽，不等同 owner 拒绝，也不代表终身尝试上限。进一步修改须先明确缺陷与合法可行的处理路径。
3. **owner 拒绝单独保留**：Star00、Touch01、Utage2P 的 owner-rejected 输出不因批次上传而重新获准。没有用技术 hold 代替 owner 判断。
4. **游戏内验收未做**：仍需 importer/BC7 重压缩、atlas、filter、sprite 边界与 pivot、材质/UV/mask、动画与状态切换、布局及实际 consumer 验证；源 metadata 的保留不等于导入成功。联合 notice 的配准还依赖实际 consumer timing。
5. **库存边界未知**：Glitter 实际像素、未确认素材/消费关系及有缺口的历史，不计为缺图，更不推算为已完成。已登记库存不是全游戏清单。

本入口所有本轮候选均为 `owner_approved: false`、`runtime_verified: false`。审阅时请先看原始图与 1× 明/暗底对照，再看具体 caveat；“候选”不等于游戏内或 owner 验收，“未发布”不计已发布。

## 11 项几何复查的具体障碍

- Setting02：文字和按钮为连续非透明区域，整体变换不能独立恢复文字比例/位置；对齐主体会使完整像素支持越界。
- Ghost02、Ghost03：主体对齐所需放大会使非零像素越出画布；裁掉或随意移动残余像素不符合保留约束。
- MyStatus_Base_02、_03：外轮廓已对齐，但内胶囊位置/端点不同；内外结构相连，空白拉伸条不授权独立移动内结构。
- BattleBase_Active：内部边界与页脚配准不同，整体变换会破坏外框对齐；拆分需切断已有轮廓/填色。
- Damage_01、_02、_03：黑槽内部尺寸/位置不符，黑槽、心形、标签与边框相连，不能独立调整而保持外框锚点。
- Recovery_02：内部黑槽与连续丝带不符，独立校正会切断不透明图形；完整支持对齐也会垂直越界。
- JackingGuage_draw_01：文字和胶囊轮廓实际接触，没有零透明度间隙；整体变换保留接触关系，拆分须切断轮廓。

**完成边界：已知静态素材的本次限定绘制/修订交付已完成，暂无新增原生绘制计划。** 这不关闭上述源契约、未知库存、owner 审阅与游戏内验证；也不承诺这些 hold 在其他授权方法下永远不可修复。
