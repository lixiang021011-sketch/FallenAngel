# 全库 timing 分诊表

只基于已经量出来的东西（来源 health、小节数、offset 段、持续多少个 matched measure、是否 reconverge），不新增测量、也不判哪条网格是真值。

## 结论：31 首里只有 0 首需要人听

| 处理结果 | 首数 | 歌 |
|---|---|---|
| 自动通过（读法之间无分歧） | 7 | dubai、hitchcock、natsuyuki、soragoto、taishou、tao、tengoku |
| 自动解释并局部降级某条来源 | 8 | flyway、flywayleo、henceforth、knowme、round2、sakuranoko、velvet、yukiguni |
| 只标 timing drift，仍可继续分析 | 4 | chichunhua、lulu、yorukake、yue |
| 登记为局部（单点 / 合回来），不升级 | 9 | eine、haiiro、happyend、iloveyou、kaluo、landaohua、onewish、sacra、spica |
| 人工已听过并定案（按人的判定收口） | 3 | kaitou、kiechau、seiza |
| **需要人工听** | 0 | — |

## 一首歌一行

| 歌 | diagnosis | 受影响区段 | 嫌疑来源 | 归因 | 人工？ | evidence（来源 / health / offset / 小节异常 / reconverge） |
|---|---|---|---|---|---|---|
| chichunhua | continuous_drift | 02:04–02:36、01:18–01:49、02:51–03:20、04:34–04:57、00:00–00:09 | — | — | 否 | BeatThis ok、madmom ok、mapperatorinator ok、workbench ok ｜ BeatThis↔madmom k=+1(support 6)、BeatThis↔madmom k=-1(support 14)、BeatThis↔madmom k=-2(support 9)、BeatThis↔madmom k=+2(support 8) ｜ 小节异常：mapperatorinator 异常小节 5 ｜ reconverge：20 处 |
| dubai | clean | — | — | — | 否 | BeatThis ok、madmom ok、mapperatorinator ok、workbench ok ｜ — ｜ 小节异常：BeatThis 异常小节 3 ｜ reconverge：无 |
| eine | reconvergence | 01:13–03:44、04:02–04:08 | — | — | 否 | BeatThis ok、madmom ok、mapperatorinator ok、workbench ok ｜ madmom↔BeatThis k=+2(support 5)、mapperatorinator↔BeatThis k=+2(support 10)、mapperatorinator↔BeatThis k=-1(support 10)、mapperatorinator↔BeatThis k=+2(support 3) ｜ 小节异常：BeatThis 异常小节 46、madmom 异常小节 46、mapperatorinator 异常小节 6 ｜ reconverge：18 处 |
| flyway | source_measure_count_error | 02:15–02:35 | BeatThis | bar-length | 否 | BeatThis ok、madmom ok、mapperatorinator ok、workbench suspect ｜ madmom↔BeatThis k=+2(support 3)、madmom↔BeatThis k=+2(support 5)、mapperatorinator↔BeatThis k=+2(support 9) ｜ 小节异常：BeatThis 异常小节 24、workbench 异常小节 0 ｜ reconverge：3 处 |
| flywayleo | source_measure_count_error | 02:18–02:33 | BeatThis | bar-length | 否 | BeatThis ok、madmom ok、mapperatorinator ok、workbench suspect ｜ madmom↔BeatThis k=+2(support 7)、mapperatorinator↔BeatThis k=+2(support 7) ｜ 小节异常：BeatThis 异常小节 9、workbench 异常小节 0 ｜ reconverge：2 处 |
| haiiro | single_flip | 04:22–04:34、04:22–04:34 | — | — | 否 | BeatThis ok、madmom ok、mapperatorinator ok、workbench suspect ｜ — ｜ 小节异常：workbench 异常小节 0 ｜ reconverge：无 |
| happyend | single_flip | 04:51–05:03、05:01–05:13、05:01–05:13 | — | — | 否 | BeatThis ok、madmom ok、mapperatorinator ok、workbench ok ｜ — ｜ 小节异常：BeatThis 异常小节 3、madmom 异常小节 13 ｜ reconverge：无 |
| henceforth | source_measure_count_error | 03:28–03:51 | BeatThis | bar-length | 否 | BeatThis ok、madmom ok、mapperatorinator ok、workbench ok ｜ madmom↔BeatThis k=+2(support 9)、madmom↔BeatThis k=+2(support 6)、mapperatorinator↔BeatThis k=+2(support 8)、workbench↔BeatThis k=+2(support 9) ｜ 小节异常：BeatThis 异常小节 2 ｜ reconverge：无 |
| hitchcock | clean | — | — | — | 否 | BeatThis ok、madmom ok、mapperatorinator ok、workbench suspect ｜ — ｜ 小节异常：BeatThis 异常小节 7、workbench 异常小节 0 ｜ reconverge：无 |
| iloveyou | reconvergence | 00:01–02:46 | — | — | 否 | BeatThis ok、madmom ok、mapperatorinator ok、workbench suspect ｜ madmom↔BeatThis k=+2(support 3)、madmom↔BeatThis k=+2(support 4)、madmom↔BeatThis k=+2(support 5)、madmom↔BeatThis k=+2(support 10) ｜ 小节异常：BeatThis 异常小节 5、mapperatorinator 异常小节 3、workbench 异常小节 0 ｜ reconverge：9 处 |
| kaitou | human_confirmed_source | 00:02–03:12 | BeatThis、madmom、mapperatorinator | human | 否 | BeatThis ok、madmom ok、mapperatorinator ok、workbench ok ｜ madmom↔mapperatorinator k=-1(support 63)、madmom↔mapperatorinator k=+1(support 26)、madmom↔workbench k=+2(support 26)、mapperatorinator↔BeatThis k=+1(support 7) ｜ 小节异常：BeatThis 异常小节 41、madmom 异常小节 3 ｜ reconverge：无 |
| kaluo | single_flip | 03:40–03:52、03:40–03:52 | — | — | 否 | BeatThis ok、madmom ok、mapperatorinator ok、workbench ok ｜ — ｜ 小节异常：BeatThis 异常小节 29 ｜ reconverge：无 |
| kiechau | human_confirmed_source | 00:01–03:35 | BeatThis、madmom、mapperatorinator | human | 否 | BeatThis ok、madmom ok、mapperatorinator ok、workbench ok ｜ madmom↔BeatThis k=-2(support 3)、madmom↔BeatThis k=+2(support 4)、madmom↔BeatThis k=+2(support 4)、madmom↔BeatThis k=+2(support 11) ｜ 小节异常：BeatThis 异常小节 17、madmom 异常小节 2、mapperatorinator 异常小节 5 ｜ reconverge：22 处 |
| knowme | source_measure_count_error | 01:33–03:22、00:02–00:22 | BeatThis | bar-length | 否 | BeatThis ok、madmom ok、mapperatorinator ok、workbench ok ｜ madmom↔BeatThis k=+2(support 7)、mapperatorinator↔BeatThis k=+2(support 8)、mapperatorinator↔BeatThis k=+2(support 38)、workbench↔BeatThis k=+2(support 8) ｜ 小节异常：BeatThis 异常小节 3、madmom 异常小节 36 ｜ reconverge：3 处 |
| landaohua | single_flip | 02:24–02:36、01:26–01:38、01:26–01:38、04:48–05:00 | — | — | 否 | BeatThis ok、madmom ok、mapperatorinator ok、workbench ok ｜ — ｜ 小节异常：BeatThis 异常小节 64、madmom 异常小节 29 ｜ reconverge：无 |
| lulu | local_timing_drift | 01:39–01:45 | — | — | 否 | BeatThis ok、madmom ok、mapperatorinator ok、workbench ok ｜ — ｜ 小节异常：BeatThis 异常小节 44、madmom 异常小节 130 ｜ reconverge：无 |
| natsuyuki | clean | — | — | — | 否 | BeatThis ok、madmom ok、mapperatorinator ok、workbench ok ｜ — ｜ 小节异常：无 ｜ reconverge：无 |
| onewish | reconvergence | 03:18–03:25 | — | — | 否 | BeatThis ok、madmom ok、mapperatorinator ok、workbench ok ｜ BeatThis↔mapperatorinator k=-2(support 3)、BeatThis↔workbench k=+1(support 5)、madmom↔workbench k=+1(support 8) ｜ 小节异常：BeatThis 异常小节 13、mapperatorinator 异常小节 1 ｜ reconverge：2 处 |
| round2 | source_measure_count_error | 01:03–02:27 | mapperatorinator | bar-length | 否 | madmom ok、mapperatorinator suspect ｜ mapperatorinator↔madmom k=+2(support 12)、mapperatorinator↔madmom k=+2(support 14)、mapperatorinator↔madmom k=+2(support 6) ｜ 小节异常：madmom 异常小节 2、mapperatorinator 异常小节 29 ｜ reconverge：3 处 |
| sacra | single_flip | 01:16–01:28、01:14–01:26 | — | — | 否 | BeatThis ok、madmom ok、mapperatorinator ok、workbench suspect ｜ — ｜ 小节异常：BeatThis 异常小节 12、workbench 异常小节 0 ｜ reconverge：无 |
| sakuranoko | source_measure_count_error | 02:34–02:38 | BeatThis | bar-length | 否 | BeatThis ok、madmom ok、mapperatorinator ok、workbench ok ｜ madmom↔BeatThis k=+2(support 3)、mapperatorinator↔BeatThis k=+2(support 3)、workbench↔BeatThis k=+2(support 3) ｜ 小节异常：BeatThis 异常小节 9 ｜ reconverge：3 处 |
| seiza | human_confirmed_source | 03:28–04:11 | BeatThis、madmom、mapperatorinator | human | 否 | BeatThis ok、madmom ok、mapperatorinator ok、workbench ok ｜ madmom↔workbench k=+2(support 6)、madmom↔workbench k=+2(support 16)、mapperatorinator↔workbench k=+2(support 22)、workbench↔BeatThis k=-2(support 6) ｜ 小节异常：BeatThis 异常小节 4 ｜ reconverge：无 |
| soragoto | clean | — | — | — | 否 | BeatThis ok、madmom ok、mapperatorinator ok、workbench ok ｜ — ｜ 小节异常：无 ｜ reconverge：无 |
| spica | single_flip | 02:33–02:45 | — | — | 否 | BeatThis ok、madmom ok、mapperatorinator ok、workbench ok ｜ — ｜ 小节异常：无 ｜ reconverge：无 |
| taishou | clean | — | — | — | 否 | BeatThis ok、madmom ok、mapperatorinator ok、workbench ok ｜ — ｜ 小节异常：无 ｜ reconverge：无 |
| tao | clean | — | — | — | 否 | BeatThis ok、madmom ok、mapperatorinator ok、workbench ok ｜ — ｜ 小节异常：无 ｜ reconverge：无 |
| tengoku | clean | — | — | — | 否 | BeatThis ok、madmom ok、mapperatorinator ok、workbench ok ｜ — ｜ 小节异常：BeatThis 异常小节 7 ｜ reconverge：无 |
| velvet | source_measure_count_error | 01:03–02:58、00:03–00:40、03:32–04:08 | BeatThis | bar-length | 否 | BeatThis ok、madmom ok、mapperatorinator ok、workbench ok ｜ madmom↔BeatThis k=+2(support 6)、madmom↔BeatThis k=+2(support 3)、madmom↔BeatThis k=-2(support 3)、mapperatorinator↔BeatThis k=+2(support 4) ｜ 小节异常：BeatThis 异常小节 32 ｜ reconverge：9 处 |
| yorukake | local_timing_drift | 03:09–03:17 | — | — | 否 | BeatThis ok、madmom ok、mapperatorinator ok、workbench ok ｜ — ｜ 小节异常：无 ｜ reconverge：无 |
| yue | local_timing_drift | 02:42–02:47 | — | — | 否 | BeatThis ok、madmom ok、mapperatorinator ok、workbench suspect ｜ — ｜ 小节异常：BeatThis 异常小节 21、workbench 异常小节 0 ｜ reconverge：无 |
| yukiguni | source_measure_count_error | 00:13–00:26、03:07–03:21、02:43–02:54、04:30–04:40 | BeatThis | bar-length | 否 | BeatThis ok、madmom ok、mapperatorinator ok、workbench ok ｜ BeatThis↔madmom k=-2(support 4)、BeatThis↔madmom k=-2(support 5)、BeatThis↔workbench k=+2(support 4)、BeatThis↔workbench k=-2(support 4) ｜ 小节异常：BeatThis 异常小节 38 ｜ reconverge：4 处 |

## 需要人听的那几首（六字段全量）

## 自动解释了谁（局部降级，不进人工）

- **flyway** → 降级 BeatThis：只在这一段降级 BeatThis 的小节线（小节长度异常 24 处（短 24 / 长 0）），其余读法互相一致；不进人工。
- **flywayleo** → 降级 BeatThis：只在这一段降级 BeatThis 的小节线（小节长度异常 9 处（短 9 / 长 0）），其余读法互相一致；不进人工。
- **henceforth** → 降级 BeatThis：只在这一段降级 BeatThis 的小节线（小节长度异常 2 处（短 2 / 长 0）），其余读法互相一致；不进人工。
- **knowme** → 降级 BeatThis：只在这一段降级 BeatThis 的小节线（小节长度异常 3 处（短 3 / 长 0）；小节头只有 72 个（同类中位数 122）），其余读法互相一致；不进人工。
- **round2** → 降级 mapperatorinator：只在这一段降级 mapperatorinator 的小节线（health=suspect；小节长度异常 29 处（短 0 / 长 29）），其余读法互相一致；不进人工。
- **sakuranoko** → 降级 BeatThis：只在这一段降级 BeatThis 的小节线（小节长度异常 9 处（短 9 / 长 0）），其余读法互相一致；不进人工。
- **velvet** → 降级 BeatThis：只在这一段降级 BeatThis 的小节线（小节长度异常 32 处（短 31 / 长 1）），其余读法互相一致；不进人工。
- **yukiguni** → 降级 BeatThis：只在这一段降级 BeatThis 的小节线（小节长度异常 38 处（短 4 / 长 34）），其余读法互相一致；不进人工。

## 只标范围、不动结论的

- **chichunhua** → continuous_drift：相位连续滑：这是 tempo / affine 假设的风险（两条线的小节长度略有出入），不是相位错位；不判小节错。
- **eine** → reconvergence：分歧之后都合回来了：只标这些区段，不把整首歌定性为异常。
- **haiiro** → single_flip：单点跳变，前后立即恢复：只登记，不当异常。
- **happyend** → single_flip：单点跳变，前后立即恢复：只登记，不当异常。
- **iloveyou** → reconvergence：分歧之后都合回来了：只标这些区段，不把整首歌定性为异常。
- **kaluo** → single_flip：单点跳变，前后立即恢复：只登记，不当异常。
- **landaohua** → single_flip：单点跳变，前后立即恢复：只登记，不当异常。
- **lulu** → local_timing_drift：B 层干净，只有 A 层显示拍子在局部漂：不判小节错，下游用到拍层结论时带保留。
- **onewish** → reconvergence：分歧都没合回来，但每段只有 3 个 matched measure 支持（低于 4）：登记为局部单点，不占人工。
- **sacra** → single_flip：单点跳变，前后立即恢复：只登记，不当异常。
- **spica** → single_flip：单点跳变，前后立即恢复：只登记，不当异常。
- **yorukake** → local_timing_drift：B 层干净，只有 A 层显示拍子在局部漂：不判小节错，下游用到拍层结论时带保留。
- **yue** → local_timing_drift：B 层干净，只有 A 层显示拍子在局部漂：不判小节错，下游用到拍层结论时带保留。

第一版不做：全库 ensemble、不信 Mapperatorinator 单次采样、不自动改任何网格、不改人工校准。分诊的降级只影响下游读这条结论时的准入，原始 observation 一个数都不动。

## 下游准入（`static/timing_admission.json`）

这是这一层的出口：下游只读它，不再自己判断哪条网格对。

- **eligible**（可以整曲用）17 首：dubai、flyway、flywayleo、henceforth、hitchcock、kaitou、kiechau、knowme、natsuyuki、sakuranoko、seiza、soragoto、taishou、tao、tengoku、velvet、yukiguni
- **caution**（能用，但标出的区段要带保留）14 首：chichunhua、eine、haiiro、happyend、iloveyou、kaluo、landaohua、lulu、onewish、round2、sacra、spica、yorukake、yue
- **ineligible**（先别用）0 首：—

