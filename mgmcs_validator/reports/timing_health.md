# 全库 timing 体检表

31 首歌，2026-10-11 那一轮：五条读法（人 / BeatThis / madmom / workbench / Mapperatorinator）齐了之后的第一张表。

分类：**1-beat phase error** 3　**2-beat counting error** 11　**clean** 13　**continuous slide** 1　**local drift** 3

只汇总现有两层诊断：**A 层** 拍子跟没跟住（ADTOF 鼓点对 affine 网格，held-out 校正固定偏移后），
**B 层** 各来源小节头两两的相位关系。分类是机械规则（见 `tools/docs/timing_health_table.py`）。
人那条（手机按的小节头）不进分类，只列在下面详情里——它多数只有几票，拿它判"这首歌是什么错"没有依据。

| 歌 | 分类 | A 层：≤0.10 / ≤0.25 | A 层漂移 | A:BeatThis 直线残差 | B 层相位 | B 层标志 | 嫌疑来源 |
|---|---|---|---|---|---|---|---|
| chichunhua | continuous slide | 21% / 52% | 局部明显 | 389 ms（0.89 拍） | 有一对分歧 | continuous drift | — |
| dubai | clean | 98% / 99% | 几乎没有 | 2157 ms（5.57 拍） | 全部一致 | — | — |
| eine | 1-beat phase error | 85% / 98% | 轻微 | 1018 ms（1.53 拍） | 有一对分歧 | phase slip（半小节）、reconvergence、phase slip（整拍） | madmom |
| flyway | 2-beat counting error | 92% / 99% | 局部明显 | 143 ms（0.23 拍） | 有一对分歧 | phase slip（半小节）、reconvergence | BeatThis |
| flywayleo | 2-beat counting error | 94% / 99% | 局部明显 | 6 ms（0.01 拍） | 有一对分歧 | phase slip（半小节）、reconvergence | BeatThis |
| haiiro | clean | 68% / 98% | 轻微 | 25 ms（0.05 拍） | 只有单点跳变 | single flip（只一处，未持续） | — |
| happyend | clean | 81% / 100% | 轻微 | 1340 ms（1.76 拍） | 只有单点跳变 | single flip（只一处，未持续） | — |
| henceforth | 2-beat counting error | 98% / 100% | 轻微 | 89 ms（0.24 拍） | 有一对分歧 | phase slip（半小节） | BeatThis |
| hitchcock | clean | 92% / 99% | 几乎没有 | 3251 ms（7.80 拍） | 全部一致 | — | — |
| iloveyou | 2-beat counting error | 72% / 98% | 轻微 | 9 ms（0.01 拍） | 有一对分歧 | phase slip（半小节）、reconvergence | — |
| kaitou | 1-beat phase error | 93% / 99% | 几乎没有 | 7 ms（0.01 拍） | 有一对分歧 | single flip（只一处，未持续）、phase slip（半小节）、phase slip（整拍）、fixed offset、unstable | BeatThis、workbench |
| kaluo | clean | 94% / 100% | 几乎没有 | 6 ms（0.01 拍） | 只有单点跳变 | single flip（只一处，未持续） | — |
| kiechau | 2-beat counting error | 66% / 94% | 局部明显 | 158 ms（0.28 拍） | 有一对分歧 | phase slip（半小节）、reconvergence | — |
| knowme | 2-beat counting error | 92% / 99% | 几乎没有 | 5 ms（0.01 拍） | 有一对分歧 | phase slip（半小节） | BeatThis |
| landaohua | clean | 94% / 100% | 几乎没有 | 7263 ms（10.71 拍） | 只有单点跳变 | single flip（只一处，未持续） | — |
| lulu | local drift | 56% / 89% | 局部明显 | 5891 ms（12.91 拍） | 只有单点跳变 | single flip（只一处，未持续） | — |
| natsuyuki | clean | 83% / 93% | 轻微 | 537 ms（1.58 拍） | 全部一致 | — | — |
| onewish | 1-beat phase error | 94% / 99% | 轻微 | 1190 ms（2.82 拍） | 有一对分歧 | phase slip（半小节）、phase slip（整拍） | — |
| round2 | 2-beat counting error | — | 无网格 | — | 有一对分歧 | phase slip（半小节）、reconvergence | madmom、mapperatorinator |
| sacra | clean | 98% / 100% | 几乎没有 | 237 ms（0.66 拍） | 只有单点跳变 | single flip（只一处，未持续） | — |
| sakuranoko | 2-beat counting error | 95% / 99% | 几乎没有 | 6 ms（0.01 拍） | 有一对分歧 | phase slip（半小节）、reconvergence | BeatThis |
| seiza | 2-beat counting error | 89% / 99% | 几乎没有 | 322 ms（0.66 拍） | 有一对分歧 | single flip（只一处，未持续）、phase slip（半小节） | workbench |
| soragoto | clean | 87% / 87% | 几乎没有 | 413 ms（0.48 拍） | 全部一致 | — | — |
| spica | clean | 99% / 100% | 几乎没有 | 5 ms（0.01 拍） | 只有单点跳变 | single flip（只一处，未持续） | — |
| taishou | clean | 99% / 100% | 几乎没有 | 6 ms（0.01 拍） | 全部一致 | — | — |
| tao | clean | 98% / 100% | 几乎没有 | 5 ms（0.01 拍） | 全部一致 | — | — |
| tengoku | clean | 73% / 98% | 轻微 | 1411 ms（2.75 拍） | 全部一致 | — | — |
| velvet | 2-beat counting error | 89% / 95% | 几乎没有 | 8 ms（0.01 拍） | 有一对分歧 | phase slip（半小节）、reconvergence | BeatThis |
| yorukake | local drift | 81% / 100% | 局部明显 | 8 ms（0.02 拍） | 全部一致 | — | — |
| yue | local drift | 45% / 88% | 局部明显 | 1743 ms（4.10 拍） | 全部一致 | — | — |
| yukiguni | 2-beat counting error | 66% / 93% | 轻微 | 2408 ms（2.60 拍） | 有一对分歧 | single flip（只一处，未持续）、phase slip（半小节）、reconvergence | BeatThis |

各来源自己的 health（只反映它自己输出得像不像话，不代表音乐真值）：

- **chichunhua**：BeatThis ok、madmom ok、mapperatorinator ok、workbench ok
- **dubai**：BeatThis ok、madmom ok、mapperatorinator ok、workbench ok
- **eine**：BeatThis ok、madmom ok、human:cal_01 ok、mapperatorinator ok、workbench ok
- **flyway**：BeatThis ok、madmom ok、mapperatorinator ok、workbench suspect
- **flywayleo**：BeatThis ok、madmom ok、human:cal_01 ok、human:cal_02 ok、mapperatorinator ok、workbench suspect
- **haiiro**：BeatThis ok、madmom ok、mapperatorinator ok、workbench suspect
- **happyend**：BeatThis ok、madmom ok、human:cal_01 ok、mapperatorinator ok、workbench ok
- **henceforth**：BeatThis ok、madmom ok、mapperatorinator ok、workbench ok
- **hitchcock**：BeatThis ok、madmom ok、human:cal_01 ok、human:cal_02 ok、mapperatorinator ok、workbench suspect
- **iloveyou**：BeatThis ok、madmom ok、human:cal_01 ok、human:cal_02 ok、human:cal_03 ok、mapperatorinator ok、workbench suspect
- **kaitou**：BeatThis ok、madmom ok、human:cal_01 ok、mapperatorinator ok、workbench ok
- **kaluo**：BeatThis ok、madmom ok、human:cal_01 ok、human:cal_02 ok、mapperatorinator ok、workbench ok
- **kiechau**：BeatThis ok、madmom ok、human:cal_01 ok、human:cal_02 ok、human:cal_03 ok、human:cal_06 suspect、human:cal_07 suspect、mapperatorinator ok、workbench ok
- **knowme**：BeatThis ok、madmom ok、mapperatorinator ok、workbench ok
- **landaohua**：BeatThis ok、madmom ok、human:cal_02 ok、human:cal_03 ok、human:cal_04 ok、mapperatorinator ok、workbench ok
- **lulu**：BeatThis ok、madmom ok、human:cal_01 ok、human:cal_04 ok、mapperatorinator ok、workbench ok
- **natsuyuki**：BeatThis ok、madmom ok、mapperatorinator ok、workbench ok
- **onewish**：BeatThis ok、madmom ok、mapperatorinator ok、workbench ok
- **round2**：madmom ok、human:cal_01 ok、human:cal_03 ok、mapperatorinator suspect
- **sacra**：BeatThis ok、madmom ok、mapperatorinator ok、workbench suspect
- **sakuranoko**：BeatThis ok、madmom ok、human:cal_02 ok、human:cal_03 ok、mapperatorinator ok、workbench ok
- **seiza**：BeatThis ok、madmom ok、human:cal_01 ok、human:cal_03 ok、mapperatorinator ok、workbench ok
- **soragoto**：BeatThis ok、madmom ok、mapperatorinator ok、workbench ok
- **spica**：BeatThis ok、madmom ok、human:cal_01 ok、human:cal_02 ok、mapperatorinator ok、workbench ok
- **taishou**：BeatThis ok、madmom ok、human:cal_01 ok、human:cal_02 ok、mapperatorinator ok、workbench ok
- **tao**：BeatThis ok、madmom ok、human:cal_01 ok、human:cal_02 ok、human:cal_04 ok、mapperatorinator ok、workbench ok
- **tengoku**：BeatThis ok、madmom ok、mapperatorinator ok、workbench ok
- **velvet**：BeatThis ok、madmom ok、human:cal_01 ok、human:cal_02 ok、mapperatorinator ok、workbench ok
- **yorukake**：BeatThis ok、madmom ok、mapperatorinator ok、workbench ok
- **yue**：BeatThis ok、madmom ok、human:cal_01 ok、human:cal_02 ok、mapperatorinator ok、workbench suspect
- **yukiguni**：BeatThis ok、madmom ok、human:cal_01 ok、mapperatorinator ok、workbench ok

## 哪首歌要听哪一段

有 B 层分歧、或 A 层有提示（局部漂移 / 拍序列不成直线）的歌列在这里；其余两层都干净的歌曲不必整曲审。

### chichunhua — continuous slide
- BeatThis↔madmom：两条小节长度略有出入：相位连续滑 -1.24 拍/分钟，最大跳变只有 0.15 拍（没有突变）｜异常区段 00:00–00:09、01:19–01:42、02:05–02:20、02:22–02:34、02:57–03:20
- BeatThis↔mapperatorinator：两条小节长度略有出入：相位连续滑 -1.24 拍/分钟，最大跳变只有 0.18 拍（没有突变）｜异常区段 00:02–00:09、01:24–01:49、02:12–02:22、02:24–02:36、03:02–03:20
- madmom↔workbench：两条小节长度略有出入：相位连续滑 +1.24 拍/分钟，最大跳变只有 0.11 拍（没有突变）｜异常区段 00:00–00:07、01:18–01:42、02:04–02:16、02:18–02:28、02:51–03:18、04:34–04:53
- mapperatorinator↔workbench：两条小节长度略有出入：相位连续滑 +1.24 拍/分钟，最大跳变只有 0.08 拍（没有突变）｜异常区段 00:02–00:09、01:23–01:44、02:11–02:19、02:21–02:34、02:57–03:20、04:37–04:57
- 人那条：没有小节头按压
- BeatThis↔madmom：5 段错位、k 走遍 -2-1+1+2、趋势 -1.24 拍/分钟、残余悬在中间 —— 这是相位滑过整数拍（两条小节长度不同），不是停错在某一格
- BeatThis↔mapperatorinator：5 段错位、k 走遍 -2-1+1+2、趋势 -1.24 拍/分钟、残余悬在中间 —— 这是相位滑过整数拍（两条小节长度不同），不是停错在某一格
- madmom↔workbench：6 段错位、k 走遍 -2-1+1+2、趋势 +1.24 拍/分钟、残余悬在中间 —— 这是相位滑过整数拍（两条小节长度不同），不是停错在某一格
- mapperatorinator↔workbench：6 段错位、k 走遍 -2-1+1+2、趋势 +1.24 拍/分钟、残余悬在中间 —— 这是相位滑过整数拍（两条小节长度不同），不是停错在某一格
- A 层：Beat This 的拍序列不能被一条直线描述（残差 389 ms）——这是拍层的事，和小节相位是两回事

### dubai — clean
- 人那条：没有小节头按压
- A 层：Beat This 的拍序列不能被一条直线描述（残差 2157 ms）——这是拍层的事，和小节相位是两回事

### eine — 1-beat phase error
- madmom↔BeatThis：局部错位后又合上（reconvergence）：错位 ±2 拍（半小节）；有一处 +2.00 拍的跳变（这才是错位） @01:29，平时 +0.06 拍/分钟｜异常区段 01:13–01:24
- mapperatorinator↔BeatThis：局部错位后又合上（reconvergence）：错位 ±2 拍（半小节）, 局部 -1 拍；有一处 +2.01 拍的跳变（这才是错位） @03:53，平时 -0.24 拍/分钟｜异常区段 02:31–02:55、03:06–03:31、03:36–03:44、04:02–04:08
- mapperatorinator↔madmom：局部错位后又合上（reconvergence）：错位 ±2 拍（半小节）, 局部 -1 拍；有一处 +2.03 拍的跳变（这才是错位） @01:20，平时 -0.24 拍/分钟｜异常区段 01:20–01:42、02:42–03:01、03:06–03:31
- mapperatorinator↔workbench：局部错位后又合上（reconvergence）：错位 ±2 拍（半小节）, 局部 +1 拍；有一处 +2.00 拍的跳变（这才是错位） @01:20，平时 -0.10 拍/分钟｜异常区段 01:28–01:33、01:46–03:31
- workbench↔BeatThis：局部错位后又合上（reconvergence）：错位 ±2 拍（半小节）, 局部 -1 拍, 局部 +1 拍；有一处 +2.01 拍的跳变（这才是错位） @03:53，平时 -0.16 拍/分钟｜异常区段 01:13–01:26、01:43–02:24、02:30–03:06、03:08–03:31、03:36–03:44
- workbench↔madmom：局部错位后又合上（reconvergence）：错位 局部 -1 拍, 局部 +1 拍, ±2 拍（半小节）；有一处 +2.16 拍的跳变（这才是错位） @04:12，平时 -0.27 拍/分钟｜异常区段 01:43–02:11、02:41–03:03、03:08–03:31
- 人那条：human:cal_01 6 票
- A 层：Beat This 的拍序列不能被一条直线描述（残差 1018 ms）——这是拍层的事，和小节相位是两回事

### flyway — 2-beat counting error
- madmom↔BeatThis：局部错位后又合上（reconvergence）：错位 ±2 拍（半小节）；有一处 +1.98 拍的跳变（这才是错位） @02:38，平时 +0.24 拍/分钟｜异常区段 02:15–02:20、02:25–02:35
- mapperatorinator↔BeatThis：局部错位后又合上（reconvergence）：错位 ±2 拍（半小节）；有一处 +1.97 拍的跳变（这才是错位） @02:15，平时 +0.24 拍/分钟｜异常区段 02:15–02:35
- 人那条：没有小节头按压
- workbench 那条校准的 source-health：Beat This 与鼓起音格子没对上（lock 0.002），这是它自己的规则，不是歌曲健康

### flywayleo — 2-beat counting error
- madmom↔BeatThis：局部错位后又合上（reconvergence）：错位 ±2 拍（半小节）；有一处 +1.99 拍的跳变（这才是错位） @02:36，平时 +0.18 拍/分钟｜异常区段 02:18–02:33
- mapperatorinator↔BeatThis：局部错位后又合上（reconvergence）：错位 ±2 拍（半小节）；有一处 +1.98 拍的跳变（这才是错位） @02:35，平时 +0.18 拍/分钟｜异常区段 02:18–02:33
- 人那条：human:cal_01 2 票、human:cal_02 7 票
- workbench 那条校准的 source-health：Beat This 与鼓起音格子没对上（lock 0.003），这是它自己的规则，不是歌曲健康

### haiiro — clean
- BeatThis↔madmom：其余全段一致，只在这一处跳了 +1.95 拍 @04:28（一处≠一段：可能就是漏了一拍），平时 -0.02 拍/分钟
- BeatThis↔mapperatorinator：其余全段一致，只在这一处跳了 +1.94 拍 @04:28（一处≠一段：可能就是漏了一拍），平时 -0.02 拍/分钟
- 人那条：没有小节头按压
- workbench 那条校准的 source-health：Beat This 与鼓起音格子没对上（lock 0.007），这是它自己的规则，不是歌曲健康
- 单点跳变（没持续成一段，先记着）：BeatThis↔madmom @04:28、BeatThis↔mapperatorinator @04:28

### happyend — clean
- BeatThis↔madmom：其余全段一致，只在这一处跳了 +1.00 拍 @04:57（一处≠一段：可能就是漏了一拍），平时 -0.02 拍/分钟
- mapperatorinator↔BeatThis：其余全段一致，只在这一处跳了 +1.98 拍 @05:07（一处≠一段：可能就是漏了一拍），平时 -0.02 拍/分钟
- workbench↔BeatThis：其余全段一致，只在这一处跳了 +1.98 拍 @05:07（一处≠一段：可能就是漏了一拍），平时 -0.02 拍/分钟
- 人那条：human:cal_01 5 票
- A 层：Beat This 的拍序列不能被一条直线描述（残差 1340 ms）——这是拍层的事，和小节相位是两回事
- 单点跳变（没持续成一段，先记着）：BeatThis↔madmom @04:57、mapperatorinator↔BeatThis @05:07、workbench↔BeatThis @05:07

### henceforth — 2-beat counting error
- madmom↔BeatThis：有一头是错位的（±2 拍（半小节） 那端没回来）；有一处 +1.91 拍的跳变（这才是错位） @03:28，平时 +0.29 拍/分钟｜异常区段 03:28–03:40、03:43–03:51
- mapperatorinator↔BeatThis：有一头是错位的（±2 拍（半小节） 那端没回来）；有一处 +1.91 拍的跳变（这才是错位） @03:28，平时 +0.17 拍/分钟｜异常区段 03:28–03:39
- workbench↔BeatThis：有一头是错位的（±2 拍（半小节） 那端没回来）；有一处 +1.94 拍的跳变（这才是错位） @03:28，平时 +0.28 拍/分钟｜异常区段 03:28–03:40、03:43–03:51
- 人那条：没有小节头按压

### hitchcock — clean
- 人那条：human:cal_01 2 票、human:cal_02 10 票
- A 层：Beat This 的拍序列不能被一条直线描述（残差 3251 ms）——这是拍层的事，和小节相位是两回事
- workbench 那条校准的 source-health：Beat This 与鼓起音格子没对上（lock 0.013），这是它自己的规则，不是歌曲健康

### iloveyou — 2-beat counting error
- madmom↔BeatThis：有一头是错位的（±2 拍（半小节） 那端没回来）；有一处 +1.92 拍的跳变（这才是错位） @00:14，平时 +1.60 拍/分钟｜异常区段 00:01–00:08、01:21–01:31、01:37–01:49、01:55–02:23
- mapperatorinator↔BeatThis：局部错位后又合上（reconvergence）：错位 ±2 拍（半小节）；有一处 +2.09 拍的跳变（这才是错位） @00:15，平时 +1.89 拍/分钟｜异常区段 00:15–01:17、02:31–02:46
- mapperatorinator↔madmom：有一头是错位的（±2 拍（半小节） 那端没回来）；有一处 +1.92 拍的跳变（这才是错位） @02:51，平时 +0.62 拍/分钟｜异常区段 00:03–01:11、01:17–01:44、01:51–02:46
- 人那条：human:cal_01 7 票、human:cal_02 6 票、human:cal_03 4 票
- workbench 那条校准的 source-health：Beat This 与鼓起音格子没对上（lock 0.004），这是它自己的规则，不是歌曲健康

### kaitou — 1-beat phase error
- madmom↔BeatThis：其余全段一致，只在这一处跳了 +1.98 拍 @00:16（一处≠一段：可能就是漏了一拍），平时 -0.44 拍/分钟
- madmom↔mapperatorinator：整段都对不上相位（几条关系轮着来）；有一处 +0.98 拍的跳变（这才是错位） @02:19，平时 +0.79 拍/分钟｜异常区段 00:03–02:14、02:19–03:12
- madmom↔workbench：有一头是错位的（±2 拍（半小节） 那端没回来）；有一处 +0.98 拍的跳变（这才是错位） @02:19，平时 +0.79 拍/分钟｜异常区段 02:19–03:12
- mapperatorinator↔BeatThis：整段都对不上相位（几条关系轮着来）；有一处 +2.03 拍的跳变（这才是错位） @02:55，平时 +1.19 拍/分钟｜异常区段 00:02–00:15、00:19–02:23、02:34–02:52、02:57–03:11
- mapperatorinator↔workbench：全段固定差 局部 +1 拍（整体相位差，不是局部错位）；不滑（-0.00 拍/分钟，最大跳变 0.03 拍）｜异常区段 00:02–03:11
- workbench↔BeatThis：有一头是错位的（±2 拍（半小节） 那端没回来）；有一处 +2.00 拍的跳变（这才是错位） @00:16，平时 -0.83 拍/分钟｜异常区段 02:33–02:52、02:56–03:11
- 人那条：human:cal_01 5 票

### kaluo — clean
- madmom↔BeatThis：其余全段一致，只在这一处跳了 +0.95 拍 @03:46（一处≠一段：可能就是漏了一拍），平时 +0.01 拍/分钟
- workbench↔BeatThis：其余全段一致，只在这一处跳了 +0.97 拍 @03:46（一处≠一段：可能就是漏了一拍），平时 +0.01 拍/分钟
- 人那条：human:cal_01 6 票、human:cal_02 5 票
- 单点跳变（没持续成一段，先记着）：madmom↔BeatThis @03:46、workbench↔BeatThis @03:46

### kiechau — 2-beat counting error
- madmom↔BeatThis：有一头是错位的（±2 拍（半小节） 那端没回来）；有一处 +1.95 拍的跳变（这才是错位） @01:01，平时 -0.42 拍/分钟｜异常区段 00:01–00:06、00:08–00:15、00:21–00:28、01:01–01:24、01:33–01:43、01:47–01:54、01:59–02:08、02:17–02:31
- madmom↔workbench：局部错位后又合上（reconvergence）：错位 ±2 拍（半小节）, ±2 拍（半小节）；有一处 +1.95 拍的跳变（这才是错位） @01:01，平时 -0.01 拍/分钟｜异常区段 01:01–01:27、01:31–01:36、01:38–01:43、01:45–01:49、01:54–02:01、02:05–02:10、02:15–02:28
- mapperatorinator↔BeatThis：局部错位后又合上（reconvergence）：错位 ±2 拍（半小节）, ±2 拍（半小节）；有一处 +1.98 拍的跳变（这才是错位） @00:29，平时 +0.17 拍/分钟｜异常区段 00:32–02:10、02:31–02:35、02:38–02:47
- mapperatorinator↔madmom：有一头是错位的（±2 拍（半小节） 那端没回来）；有一处 +1.94 拍的跳变（这才是错位） @01:00，平时 -0.84 拍/分钟｜异常区段 01:03–01:28、01:32–02:09、02:49–03:21、03:25–03:35
- mapperatorinator↔workbench：有一头是错位的（±2 拍（半小节） 那端没回来）；有一处 +1.82 拍的跳变（这才是错位） @02:12，平时 +2.13 拍/分钟｜异常区段 02:12–02:19、02:49–03:09、03:14–03:35
- workbench↔BeatThis：有一头是错位的（±2 拍（半小节） 那端没回来）；有一处 +1.95 拍的跳变（这才是错位） @03:36，平时 -0.36 拍/分钟｜异常区段 00:01–00:28
- 人那条：human:cal_01 7 票、human:cal_02 7 票、human:cal_03 3 票、human:cal_06 11 票、human:cal_07 7 票

### knowme — 2-beat counting error
- madmom↔BeatThis：有一头是错位的（±2 拍（半小节） 那端没回来）；有一处 +2.01 拍的跳变（这才是错位） @01:33，平时 +0.42 拍/分钟｜异常区段 00:02–00:19
- mapperatorinator↔BeatThis：有一头是错位的（±2 拍（半小节） 那端没回来）；有一处 +2.02 拍的跳变（这才是错位） @00:25，平时 +0.46 拍/分钟｜异常区段 00:02–00:22、01:33–03:22
- workbench↔BeatThis：有一头是错位的（±2 拍（半小节） 那端没回来）；有一处 +2.01 拍的跳变（这才是错位） @00:25，平时 +0.42 拍/分钟｜异常区段 00:02–00:22、01:33–03:22
- 人那条：没有小节头按压

### landaohua — clean
- BeatThis↔madmom：其余全段一致，只在这一处跳了 +2.00 拍 @02:30（一处≠一段：可能就是漏了一拍），平时 +0.00 拍/分钟
- BeatThis↔mapperatorinator：其余全段一致，只在这一处跳了 +1.96 拍 @01:32（一处≠一段：可能就是漏了一拍），平时 -0.01 拍/分钟
- BeatThis↔workbench：其余全段一致，只在这一处跳了 +1.96 拍 @01:32（一处≠一段：可能就是漏了一拍），平时 -0.01 拍/分钟
- workbench↔madmom：其余全段一致，只在这一处跳了 +1.99 拍 @04:54（一处≠一段：可能就是漏了一拍），平时 +0.03 拍/分钟
- 人那条：human:cal_02 6 票、human:cal_03 8 票、human:cal_04 7 票
- A 层：Beat This 的拍序列不能被一条直线描述（残差 7263 ms）——这是拍层的事，和小节相位是两回事
- 单点跳变（没持续成一段，先记着）：BeatThis↔madmom @02:30、BeatThis↔mapperatorinator @01:32、BeatThis↔workbench @01:32、workbench↔madmom @04:54

### lulu — local drift
- BeatThis↔mapperatorinator：其余全段一致，只在这一处跳了 +1.99 拍 @03:27（一处≠一段：可能就是漏了一拍），平时 -0.01 拍/分钟
- BeatThis↔workbench：其余全段一致，只在这一处跳了 +1.96 拍 @03:27（一处≠一段：可能就是漏了一拍），平时 -0.01 拍/分钟
- 人那条：human:cal_01 4 票、human:cal_04 4 票
- A 层：Beat This 的拍序列不能被一条直线描述（残差 5891 ms）——这是拍层的事，和小节相位是两回事

### natsuyuki — clean
- 人那条：没有小节头按压
- A 层：Beat This 的拍序列不能被一条直线描述（残差 537 ms）——这是拍层的事，和小节相位是两回事

### onewish — 1-beat phase error
- BeatThis↔mapperatorinator：有一头是错位的（±2 拍（半小节） 那端没回来）；有一处 +2.00 拍的跳变（这才是错位） @03:18，平时 -0.52 拍/分钟｜异常区段 03:18–03:25
- BeatThis↔workbench：有一头是错位的（局部 +1 拍 那端没回来）；有一处 +0.99 拍的跳变（这才是错位） @00:14，平时 -0.06 拍/分钟｜异常区段 00:00–00:11
- madmom↔workbench：有一头是错位的（局部 +1 拍 那端没回来）；有一处 +0.97 拍的跳变（这才是错位） @00:14，平时 -0.09 拍/分钟｜异常区段 00:00–00:12
- 人那条：没有小节头按压
- A 层：Beat This 的拍序列不能被一条直线描述（残差 1190 ms）——这是拍层的事，和小节相位是两回事

### round2 — 2-beat counting error
- mapperatorinator↔madmom：局部错位后又合上（reconvergence）：错位 ±2 拍（半小节）；有一处 +1.92 拍的跳变（这才是错位） @01:00，平时 -1.34 拍/分钟｜异常区段 01:03–01:28、01:32–02:07、02:16–02:27
- 人那条：human:cal_01 2 票、human:cal_03 3 票

### sacra — clean
- madmom↔BeatThis：其余全段一致，只在这一处跳了 +2.00 拍 @01:22（一处≠一段：可能就是漏了一拍），平时 +0.03 拍/分钟
- mapperatorinator↔BeatThis：其余全段一致，只在这一处跳了 +2.06 拍 @01:20（一处≠一段：可能就是漏了一拍），平时 -1.94 拍/分钟
- 人那条：没有小节头按压
- A 层：Beat This 的拍序列不能被一条直线描述（残差 237 ms）——这是拍层的事，和小节相位是两回事
- workbench 那条校准的 source-health：Beat This 与鼓起音格子没对上（lock 0.008），这是它自己的规则，不是歌曲健康
- 单点跳变（没持续成一段，先记着）：madmom↔BeatThis @01:22、mapperatorinator↔BeatThis @01:20

### sakuranoko — 2-beat counting error
- madmom↔BeatThis：局部错位后又合上（reconvergence）：错位 ±2 拍（半小节）；有一处 +2.00 拍的跳变（这才是错位） @02:31，平时 -0.03 拍/分钟｜异常区段 02:35–02:38
- mapperatorinator↔BeatThis：局部错位后又合上（reconvergence）：错位 ±2 拍（半小节）；有一处 +1.99 拍的跳变（这才是错位） @02:31，平时 -0.03 拍/分钟｜异常区段 02:34–02:38
- workbench↔BeatThis：局部错位后又合上（reconvergence）：错位 ±2 拍（半小节）；有一处 +2.01 拍的跳变（这才是错位） @02:31，平时 -1.36 拍/分钟｜异常区段 02:34–02:38
- 人那条：human:cal_02 4 票、human:cal_03 5 票

### seiza — 2-beat counting error
- madmom↔workbench：有一头是错位的（±2 拍（半小节） 那端没回来）；有一处 +1.96 拍的跳变（这才是错位） @03:28，平时 -0.43 拍/分钟｜异常区段 03:28–03:38、03:42–04:11
- mapperatorinator↔madmom：其余全段一致，只在这一处跳了 +1.95 拍 @03:30（一处≠一段：可能就是漏了一拍），平时 +0.85 拍/分钟
- mapperatorinator↔workbench：有一头是错位的（±2 拍（半小节） 那端没回来）；有一处 +1.96 拍的跳变（这才是错位） @03:30，平时 -0.41 拍/分钟｜异常区段 03:30–04:11
- workbench↔BeatThis：有一头是错位的（±2 拍（半小节） 那端没回来）；有一处 +1.96 拍的跳变（这才是错位） @03:29，平时 -0.41 拍/分钟｜异常区段 03:37–03:47、03:53–04:01、04:05–04:10
- 人那条：human:cal_01 5 票、human:cal_03 4 票
- A 层：Beat This 的拍序列不能被一条直线描述（残差 322 ms）——这是拍层的事，和小节相位是两回事

### soragoto — clean
- 人那条：没有小节头按压
- A 层：Beat This 的拍序列不能被一条直线描述（残差 413 ms）——这是拍层的事，和小节相位是两回事

### spica — clean
- BeatThis↔workbench：其余全段一致，只在这一处跳了 +0.96 拍 @02:39（一处≠一段：可能就是漏了一拍），平时 -0.02 拍/分钟
- 人那条：human:cal_01 6 票、human:cal_02 5 票
- 单点跳变（没持续成一段，先记着）：BeatThis↔workbench @02:39

### tengoku — clean
- 人那条：没有小节头按压
- A 层：Beat This 的拍序列不能被一条直线描述（残差 1411 ms）——这是拍层的事，和小节相位是两回事

### velvet — 2-beat counting error
- madmom↔BeatThis：局部错位后又合上（reconvergence）：错位 ±2 拍（半小节）, ±2 拍（半小节）；有一处 +2.06 拍的跳变（这才是错位） @03:01，平时 +1.25 拍/分钟｜异常区段 02:06–02:22、02:32–02:39、02:42–02:48
- mapperatorinator↔BeatThis：有一头是错位的（±2 拍（半小节） 那端没回来）；有一处 +2.01 拍的跳变（这才是错位） @00:43，平时 -0.30 拍/分钟｜异常区段 00:30–00:40、01:03–01:22、01:29–01:55、03:32–04:08
- workbench↔BeatThis：有一头是错位的（±2 拍（半小节） 那端没回来）；有一处 +1.99 拍的跳变（这才是错位） @02:29，平时 -0.13 拍/分钟｜异常区段 00:03–00:26、02:00–02:26、02:32–02:58
- 人那条：human:cal_01 6 票、human:cal_02 12 票

### yorukake — local drift
- 人那条：没有小节头按压

### yue — local drift
- 人那条：human:cal_01 7 票、human:cal_02 7 票
- A 层：Beat This 的拍序列不能被一条直线描述（残差 1743 ms）——这是拍层的事，和小节相位是两回事
- workbench 那条校准的 source-health：Beat This 与鼓起音格子没对上（lock 0.012），这是它自己的规则，不是歌曲健康

### yukiguni — 2-beat counting error
- BeatThis↔madmom：局部错位后又合上（reconvergence）：错位 ±2 拍（半小节）；有一处 +2.10 拍的跳变（这才是错位） @01:19，平时 -1.16 拍/分钟｜异常区段 02:43–02:54、03:07–03:21
- BeatThis↔mapperatorinator：其余全段一致，只在这一处跳了 +2.00 拍 @00:26（一处≠一段：可能就是漏了一拍），平时 -0.04 拍/分钟
- BeatThis↔workbench：有一头是错位的（±2 拍（半小节） 那端没回来）；有一处 +2.09 拍的跳变（这才是错位） @04:30，平时 -1.08 拍/分钟｜异常区段 00:13–00:26、02:43–02:54、04:30–04:40
- 人那条：human:cal_01 10 票
- A 层：Beat This 的拍序列不能被一条直线描述（残差 2408 ms）——这是拍层的事，和小节相位是两回事

