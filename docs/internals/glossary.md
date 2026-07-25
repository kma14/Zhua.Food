# zhua.food — 术语表 / glossary（统一口径）

**这是术语的唯一口径。** 讨论、文档、commit 里都用这一列英文 + 这一列中文，不要再换别的说法。英文是代码里的名字（`Product`/`Item`/`Chain`…），中文是固定的中文叫法。（遵循「技术词用英文、其余中文」的约定。）

## 三层数据模型 / the three layers

价格只存在最底层，上面两层是导航/归组。

| 英文（代码名） | 中文 | 一句话定义 |
|---|---|---|
| **Product** | 门店商品 | 某一个门店里的真实商品 listing，**有价格**。是我们搜索和展示的东西。旧名 `StoreProduct`。 |
| **Item** | 聚合商品 | 把「各门店的同一个商品」归到一起的**内部键**，**没有价格**，**用户永远看不到**。它的作用是让我们能算跨店最低价/比价/价格历史。旧名 `CanonicalProduct`。 |
| **Category** | 分类 | 全站共享的浏览分类树（部门→货道→货架），**是用户能看到、我们自己维护的**唯一策展面。旧名 `CanonicalCategory`。 |

> 一句话记：**门店商品有价格、聚合商品是内部归组、分类给用户浏览。**

## 门店与连锁 / stores & chains

这两个我之前混用了，固定下来：

| 英文（代码名） | 中文 | 定义 |
|---|---|---|
| **Store** | 门店 | 一家实体店。例：PAK'nSAVE Albany。代码 `Store`。 |
| **Chain** | 连锁 | 招牌/平台。我们有四个：Woolworths、New World、PAK'nSAVE、FreshChoice。代码 `Chain`，**对用户的 API 里叫 `supermarket`（超市）**。 |
| **cross-store price comparison** | 跨店比价 | 产品的核心功能：同一个商品在不同门店的价格对比。**这是护城河。** |

> 关键区别:
> - **New World / PAK'nSAVE 每家连锁有 3 个独立定价的门店** → 同连锁不同门店也有价差，也算跨店比价。
> - **Woolworths 全国统一定价** → 只开 1 个活跃门店（多开也一样价），所以 WW 商品的比价价值**只可能来自跨连锁**。
> - **FreshChoice 只有 1 个门店**（Hauraki Corner）。
>
> 所以说"跨店比价"时,既包括**同连锁跨门店**(NW/PAK),也包括**跨连锁**。要特指后者就说**跨连锁**。

## 匹配 / matching

把门店商品归到聚合商品的过程。相关术语：

| 英文（代码/概念名） | 中文 | 定义 |
|---|---|---|
| **match / matching** | 匹配 | 判断「这些门店商品是同一个商品」并归到一个聚合商品下。 |
| **anchor**（名词/动词） | 锚 / 锚定 | 一个门店商品在一个聚合商品里的**角色**——「这个聚合商品是以它为根建出来的」。**精确说法：** 建的是**一个新聚合商品（新 Item）**，这个门店商品是它的**锚**；说「以 Woolworths 为锚建聚合商品」，不要简写成「建新锚」（那会把「锚=角色」和「聚合商品=被建出来的东西」混掉）。 |
| **anchor-priority cascade** | 匹配级联 | 建聚合商品的优先顺序，按数据质量排：**Foodstuffs → Woolworths → FreshChoice**。高质量的先当锚，低质量的先去挂上高质量的。 |
| **Tier 1 / 2 / 3 / 4** | 第 1 / 2 / 3 / 4 层 | 级联的四层（见下方专表）。 |
| **attach** | 挂载 / 归入 | 一个门店商品**挂到已存在的**聚合商品上（不新建）。例：FreshChoice 商品挂到一个 Foodstuffs 聚合商品上。 |
| **singleton** | 单店聚合 | 只含**一个**门店商品的聚合商品（还没配到别的店）。 |
| **guard** | 护栏 | 那条正确性规则：品牌若属于某个更高层（Foodstuffs，FreshChoice 建锚时还含 Woolworths），就不给这个门店商品建新聚合商品（不让它当锚）——否则会造重复、拆散比价。 |
| **orphan** | 未关联商品 | 没有 `ItemId` 的门店商品（没归进任何聚合商品）。分两种：**待审商品** + **悬空商品**（见下）。 |
| **pending-review** | 待审商品 | matcher 找到了候选、但拿不准 → 进审核队列等**人**判断。未关联。 |
| **held** | 悬空商品 | 品牌是已知品牌但没匹配上、连候选都没有，被护栏按住不建独立聚合商品 → 等**算法**（尺寸归一化）改进后自动挂上。未关联。 |
| **MatchKey** | 匹配键 | 聚合商品的稳定身份串，让每轮 matcher 重跑不重复建：`foodstuffs:{sku}` / `woolworths:{sku}` / `freshchoice:{sku}` / `manual:{guid}`。 |
| **review queue** | 审核队列 | matcher 拿不准的配对，进 `MatchCandidate` 等人工判断。里面装的就是**待审商品**。 |
| **brand inference** | 品牌推断 | FreshChoice 源数据没有品牌字段，从商品名开头 1–3 个词猜品牌（D29）。 |

一个门店商品经过 matcher 后，只会落到以下**五种状态之一**（互斥、穷尽）：**① 属于 Foodstuffs 聚合商品**、**② 属于 Woolworths 聚合商品**、**③ 属于 FreshChoice 聚合商品（单店）**、**④ 待审商品**、**⑤ 悬空商品**。前三种是已关联，后两种是未关联。

### 匹配级联的四层 / the four tiers

| 层 | 锚 | 谁加入 | 产出 |
|---|---|---|---|
| **Tier 1** | Foodstuffs（`foodstuffs:{sku}`） | New World + PAK'nSAVE 共享 productId | 多门店聚合 |
| **Tier 2** | *挂到 Foodstuffs 聚合商品上* | Woolworths / FreshChoice 按 品牌+尺寸+名称 | 跨连锁比价 |
| **Tier 3** | Woolworths（`woolworths:{sku}`） | Foodstuffs 没有的 WW 商品建锚；FreshChoice 再挂上来 | WW+FC 比价 + WW 单店聚合 |
| **Tier 4** | FreshChoice（`freshchoice:{sku}`） | 前面都挂不上的 FC 商品 | FC 单店聚合 |

### 生鲜匹配 / fresh-produce matching（2026-07-24）

散装/称重生鲜没有品牌、尺寸也 loose，`品牌+尺寸`那套挂不上，于是同一根西兰花在各连锁各成一个聚合商品。生鲜走一条**按规范化名字匹配**的专门路径（在 Tier 2 之后、Tier 3 之前）。相关术语：

| 英文（代码/概念名） | 中文 | 定义 |
|---|---|---|
| **produce-path eligibility** | 生鲜准入条件 | 一条 listing 要不要走生鲜匹配路径的入口判断：**在生鲜部门 且 loose size 且 品牌非真品牌**，三条同时满足才走。否则继续走 `品牌+尺寸`老路。代码 `ProductNormalizer.Produce*` / `ItemMatcher.Produce()`。 |
| **real brand** | 真品牌 | 真实第三方厂商（Hellers、Anchor、Silver Fern Farms）。**有区分度** → 是打包货,不走生鲜路径。 |
| **private label** | 私有品牌 | 零售商自有牌(Woolworths、Macro、Pams)。是真牌,但同一根生鲜换个店就换个私牌 → **生鲜里当噪声剥掉**;**生鲜之外仍当真品牌**(打包货靠它区分)。 |
| **pseudo-brand** | 伪品牌 | 品牌字段里塞的品类词(`fresh vegetable`/`fresh fruit`/`produce`),压根不是牌子 → **哪里都当噪声**。 |
| **canonical produce name** | 生鲜规范名 | 生鲜名字剥掉伪品牌/私牌/填充/尺寸、保留区分词(organic/premium/部位/品种)、单复数归一、排序后的 token 集合。**精确整套相等**才算同款(`broccoli` ≠ `broccoli head`)。代码 `NormalizeProduceName`。 |
| **fresh department** | 生鲜部门 | 粗粒度跨连锁稳定的部门桶:Produce(果蔬)/ Protein(肉·禽·海鲜)。判定用门店自己的部门树,不用共享 Category(WW 只映射 ~26%)。 |
| **produce re-home** | 生鲜改嫁 | 之前自锚成 `woolworths:`/`freshchoice:` 单店的生鲜,重跑时拆掉自锚、改挂到 Foodstuffs 聚合商品(reclaim 的一种)。 |

## 抓取 / crawling（常用几个）

| 英文 | 中文 | 定义 |
|---|---|---|
| **crawl** | 抓取 | 从一家门店的网站拉取当前商品+价格。 |
| **crawler** | 抓取器 | 每个连锁一个。 |
| **ScrapeResult** | 抓取结果 | 一次抓取的产物：商品 + 覆盖缺口（gaps）。 |
| **gap** | 覆盖缺口 | 抓取时某页/某分类失败没抓到（D28）；有 gap 的抓取记为 `Partial`，不能算成功。 |
| **reconciliation** | 缺失对账 | 只在完整抓取后，处理「库里有、这轮没抓到」的商品（连续两轮没见→下架，D28）。 |
| **promo / special** | 促销 / 特价 | `PromoType`：None / Special（公开特价）/ MemberPrice（会员价）/ Multibuy（多买优惠）。 |

## Decision log

- **2026-07-22 — 🧑‍⚖️ (Kevin)** 建这个术语表，作为**唯一口径**。起因：讨论里「锚定 / 锚 / singleton / 跨店 / 跨连锁」等说法变来变去，需要固定中英文对照，以后统一。同时明确了 store（门店）vs chain（连锁）之前被混用。
- **2026-07-24 — 🧑‍⚖️ (Kevin: "Regime gate 语义不明，换一个同义词" / "私有品牌呢")** 加入**生鲜匹配**一节：把 "regime gate" 正名为**生鲜准入条件**（produce-path eligibility），并固定 **真品牌 / 私有品牌 / 伪品牌** 三分（Kevin 逐个追问确认）。设计与实现见 [matching.md](matching.md#fresh-produce-matching-2026-07-24)。

---

*新术语出现、或某个叫法要改时，先改这里，再全局统一。*
