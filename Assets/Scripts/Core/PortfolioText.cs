namespace FallenAngel.Core
{
    /// <summary>成长验证入口的本地化回退。正式语言资产可用同名key覆盖。</summary>
    public static class PortfolioText
    {
        public static void Register()
        {
            Add("mapTitle", "FALLEN ANGEL  /  旅程地图", "FALLEN ANGEL / JOURNEY MAP");
            Add("mapIntro", "三层十六站 · 一站接一站 · 走进终房结算", "Three layers · sixteen stops · walk into the finale to settle");
            Add("talentsPage", "查看永久天赋", "Permanent talents");
            Add("backToMap", "返回旅程地图", "Back to journey map");
            Add("chooseRoom", "选择下方房间", "Choose a room below");
            Add("leaveRoom", "离开房间", "Leave room");
            Add("mapCash", "局内现金：{0} · 点击亮起的相连房间前进", "Run cash: {0} · Choose a highlighted adjacent room");
            Add("routeFee", "路费 {0}", "Fee {0}");
            Add("currentRoom", "当前位置", "Current room");
            Add("confirmRoute", "进入 {0} 的付费路线？\n消耗 {1} 现金，余额 {2} → {3}。\n挑战失败不退路费。", "Enter the paid route to {0}?\nSpend {1} cash: {2} → {3}.\nNo fee refund on failure.");
            Add("cashShort", "局内现金不足，请选择免费路线。", "Not enough run cash. Choose a free route.");
            Add("node.START", "起点", "Start");
            Add("node.STAGE", "战斗", "Battle");
            Add("node.HARD", "硬仗", "Hard fight");
            Add("node.EVENT", "事件房", "Event");
            Add("node.REST", "休息房", "Rest");
            Add("node.FINAL", "终房", "Journey's end");
            Add("node.SHOP", "商店", "Shop");
            Add("node.EMPTY", "空占位房", "Empty room");
            // 地图节点标签（面板按节点类型取这两条；缺了会直接显示 key）
            Add("mapEmpty", "空占位房", "Empty");
            Add("mapUnknown", "？", "?");
            // ── 房间内容（占位房）与选项：文案走表，改这里或改语言文件都行 ──
            Add("room.op.leave", "离开", "Leave");
            Add("room.op.look", "看一眼", "Take a look");
            Add("room.op.take.hotbag", "拿走热水袋", "Take the hot-water bag");
            Add("room.op.take.bandage", "拿走创可贴", "Take the bandages");
            Add("room.op.take.pliers", "拿走折叠谱架", "Take the folding stand");
            Add("room.rc.empty.scene", "空占位房", "Empty room");
            Add("room.rc.empty.text", "屋里什么都没有。风从窗户进来，又原样出去。", "Nothing here. The wind comes in and leaves the same way.");
            Add("room.rc.visited.scene", "有人来过", "Someone was here");
            Add("room.rc.visited.text", "桌上有一杯没喝完的水。人刚走，或者走了很久。", "A half-finished glass of water. They just left, or left long ago.");
            Add("room.rc.gift.scene", "馈赠占位房", "Gift (placeholder)");
            Add("room.rc.gift.text", "门口放着一个纸箱，没人认领。", "A cardboard box by the door, unclaimed.");
            Add("room.rc.rest.scene", "歇脚占位房", "Rest (placeholder)");
            Add("room.rc.rest.text", "床单是干净的。今晚不用赶路。", "Clean sheets. No need to hurry tonight.");
            Add("room.rc.end.scene", "落点占位房", "Journey's end (placeholder)");
            Add("room.rc.end.text", "走到这里，这一程就算完了。", "This is as far as this trip goes.");
            // ── 层过渡页（照集成战略：时刻 + 层名 + 引言 + 该层失败文案）──
            Add("layer.1.name", "放学路上的风", "Wind on the way home");
            Add("layer.1.intro", "天还没黑。你戴上耳机，路还是你认识的那条路。", "Not dark yet. You put your headphones on; the road is still the one you know.");
            Add("layer.1.fail", "你在熟悉的路口停住了。这一程没走完。", "You stopped at a familiar corner. This trip ends here.");
            Add("layer.2.name", "灯亮起来的地方", "Where the lights are on");
            Add("layer.2.intro", "商场早就关门了，可中庭的灯还亮着。没有人，但有人来过。", "The mall closed hours ago, but the atrium lights are still on. Nobody here, but somebody was.");
            Add("layer.2.fail", "灯灭了。你还在原地。", "The lights went out. You are still standing there.");
            Add("layer.3.name", "不是时间的那个点", "The hour that isn't");
            Add("layer.3.intro", "末班车早开走了。你还在走，路已经不像刚才那条了。", "The last bus left long ago. You keep walking; the road is not the same one anymore.");
            Add("layer.3.fail", "路把你留下了。", "The road kept you.");
            Add("layer.final.name", "这一程的落点", "Where this trip lands");
            Add("layer.final.intro", "到这里，钟不走了。", "Here, the clock stops.");
            Add("layer.final.fail", "（终房不会失败）", "(the finale cannot fail)");
            Add("mapPerformance", "演奏房", "Performance");
            Add("mapChallenge", "付费演奏", "Paid performance");
            Add("room.SHOP", "已进入商店。点击「商店」按钮查看商品。", "Shop reached. Use the Shop button to browse.");
            Add("room.EMPTY", "这是一间空房，目前没有事件。", "An empty room. No event here yet.");
            Add("room.EVENT", "这里有人来过，但没留下什么。\n（事件内容还没接，关闭后继续前进）", "Someone was here once.\n(No events wired yet — close to move on.)");
            Add("room.REST", "歇一晚。\n（休息内容还没接，关闭后继续前进）", "A night's rest.\n(No rest effects yet — close to move on.)");
            Add("room.HARD", "人多，灯亮。这一场比平时难一档。", "Crowded and bright. One notch harder than usual.");
            Add("room.FINAL", "这一程的落点。\n（离开即结算；纪念品与册子留到内容期）", "The end of this journey.\n(Leaving settles the run; keepsakes come later.)");
            Add("mapLegend", "亮色：可进入 · 绿色：当前位置\n直线推进，没有分叉；战斗仍共用占位鼓谱。", "Bright: reachable · Green: current\nOne straight road, no forks. Battles share the placeholder chart.");
            Add("legacyRun", "此存档是旧版九节点地图的一局；结束本局后，新一局使用章节地图。", "This profile is on the legacy nine-node map. The next run uses the chapter map.");
            Add("entry", "成长流程验证", "Growth playtest");
            Add("title", "FALLEN ANGEL  /  永久成长", "FALLEN ANGEL  /  PERMANENT GROWTH");
            Add("intro", "三层十六站 · 七场演奏（两场硬仗）· 共用占位鼓谱", "Three layers · sixteen stops · seven performances (two hard) · shared placeholder chart");
            Add("profiles", "存档列表", "Profiles");
            Add("create", "新建存档", "Create profile");
            Add("name", "输入存档名称（最多32字）", "Profile name (up to 32 characters)");
            Add("defaultProfileName", "默认档案", "Default profile");
            Add("empty", "每个存档独立保存成长积分和永久天赋。", "Each profile has independent growth points and permanent talents.");
            Add("back", "返回主菜单", "Main menu");
            Add("balance", "{0}  ·  成长积分 {1}", "{0}  ·  Growth points {1}");
            Add("equipment", "装备 {0}/{1}", "Equipment {0}/{1}");
            Add("equipFull", "装备已满，无法购买。", "Equipment is full; cannot purchase.");
            // 商店页现金胶囊：面板标题已由 shopPanel.title 单独显示，这里只放简短金额，避免 240 宽胶囊内换行。
            Add("shopTitle", "现金 {0}", "Cash {0}");
            Add("shopOpen", "商店", "Shop");
            Add("shopLeave", "不买了，走了", "Done shopping, leave");
            Add("shopRefresh", "整批刷新（{0}）", "Refresh all ({0})");
            Add("confirmRefresh", "确认整批刷新？\n剩余次数 {0} → {1}\n按商店权重重新抽取商品。", "Refresh all?\nUses {0} → {1}.\nRe-roll goods by shop weights.");
            Add("shopInsufficient", "资金不足，无法购买。", "Not enough cash to buy.");
            Add("shopFailed", "购买未完成，请重试。", "Purchase failed; retry.");
            Add("shopSoldOut", "商店商品已售罄", "Shop sold out");
            Add("confirmPurchase", "确认购买 {0}？\n价格 {1} · 余额 {2} → {3}\n购买后立即生效，本局不可退回。", "Buy {0}?\nPrice {1} · Cash {2} → {3}\nTakes effect immediately; no refund this run.");
            Add("begin", "开始一局", "Start run");
            Add("play", "开始第 {0} / {1} 次演奏", "Play song {0} / {1}");
            Add("continue", "继续游戏", "Continue run");
            Add("abandon", "放弃本局", "Abandon run");
            Add("resume", "继续演奏", "Resume");
            Add("startSong", "开始演奏", "Start song");
            Add("paused", "演奏已暂停", "Performance paused");
            Add("pauseNote", "可继续或放弃本局；强行关闭将按演出失败结算。", "Resume or abandon. Force quitting counts as a failed run.");
            Add("pending", "已完成 {0} / {1} 次演奏 · 待结算积分 {2}", "Completed {0} / {1} songs · Pending points {2}");
            Add("nextReward", "本关完成：+{0} 积分 · 失误上限：{1}", "On completion: +{0} points · Failure limit: {1}");
            Add("nextRewardUnlimited", "本关完成：+{0} 积分 · 失误失败限制已关闭", "On completion: +{0} points · Failure limit disabled");
            Add("songResult", "本次演奏完成  +{0}", "Performance complete  +{0}");
            Add("income.title", "本关收益明细", "Stage income breakdown");
            Add("income.I0", "整曲Perfect达标奖励", "Whole-song Perfect goal");
            Add("income.J0", "付费挑战奖励", "Paid challenge bonus");
            Add("income.D0", "演奏增幅", "Performance bonus");
            Add("income.E05", "无Miss增幅", "No-miss bonus");
            Add("income.G0", "保底补足", "Floor top-up");
            Add("income.cap", "封顶裁剪", "Cap trim");
            Add("income.C1", "利息", "Interest");
            Add("income.E09", "装备利息", "Equipment interest");
            Add("income.total", "本关收入合计  +{0}", "Stage income total  +{0}");
            Add("optionalPurchase", "使用 K1 折扣（剩 {0} 次）· 价 {1}", "Use K1 discount ({0} left) · {1}");
            Add("optionalRoute", "使用 E08 减免（剩 {0} 次）· 路费 {1}", "Use E08 discount ({0} left) · fee {1}");
            Add("effectNotLive", "本效果尚未接入结算，仅作设计展示。", "This effect is not applied in this build; display only.");
            Add("drop.equipment", "关卡掉落：装备 {0}", "Stage drop: equipment {0}");
            Add("drop.cash", "关卡掉落：现金 +{0}", "Stage drop: cash +{0}");
            Add("bankNote", "积分将在整局结束时入账；现在放弃也保留已完成关卡的积分。", "Points are credited at run end. Abandoning keeps points from completed songs.");
            Add("CLEARED", "路线完成", "Route cleared");
            Add("FAILED", "演出失败", "Performance failed");
            Add("ABANDONED", "本局已放弃", "Run abandoned");
            Add("INTERRUPTED", "上次演奏意外中断，按失败结算", "Previous performance interrupted; run counted as failed");
            Add("credited", "关卡积分 {0} + 通关奖励 {1} = 已入账 {2}", "Stage points {0} + Clear bonus {1} = Credited {2}");
            Add("unlocked", "已永久解锁", "Permanently unlocked");
            Add("detail", "点击任意节点查看解锁条件。\n连线仅向下推进；交汇后可转入其他路线。", "Select a node to view its requirements.\nEdges lead downwards; junctions open new routes.");
            Add("cost", "解锁成本：{0}\n前置：{1}", "Unlock cost: {0}\nPredecessors: {1}");
            Add("none", "无", "None");
            Add("effect", "设计效果\n{0}", "Designed effect\n{0}");
            Add("score", "本次分数 {0} · 准确率 {1:0.00}%", "Score {0} · Accuracy {1:0.00}%");
            Add("effectPending", "已接入的天赋会在商店、路费与曲终结算中生效；详情页对未接线效果另有标注。", "Wired talents apply in shop, route fees and song settlement. Unwired effects are marked in the detail pane.");
            Add("unlock", "永久解锁", "Unlock permanently");
            Add("confirmUnlock", "确认解锁 {0}？\n消耗 {1} 积分，余额 {2} → {3}。\n永久解锁，无退款。", "Unlock {0}?\nSpend {1} points. Balance {2} → {3}.\nPermanent unlock; no refund.");
            Add("confirmAbandon", "确认放弃本局？\n保留已完成关卡的积分，当前未完成演奏不计分。", "Abandon this run?\nKeep points from completed songs; the unfinished song gives no points.");
            Add("confirm", "确认", "Confirm");
            Add("cancel", "取消", "Cancel");
            Add("error", "操作未完成，请重试。进度未被当作成功处理。", "Operation not completed. Retry; no success has been assumed.");
            Add("retry", "重试操作", "Retry operation");
            Add("playing", "演奏 {0}/{1}  ·  失误 {2}/{3}  ·  SPACE 暂停", "Song {0}/{1} · Failures {2}/{3} · SPACE to pause");
            Add("playingUnlimited", "演奏 {0}/{1}  ·  失误 {2}（不触发失败）  ·  SPACE 暂停", "Song {0}/{1} · Failures {2} (no failure limit) · SPACE to pause");
            Add("loading", "准备演奏…", "Preparing performance…");
            Add("missingChart", "谱面或音频无法加载，请检查资源。", "Unable to load chart or audio.");
            Add("status.Available", "可以解锁", "Available");
            Add("status.AlreadyUnlocked", "已永久解锁", "Already unlocked");
            Add("status.RunInProgress", "本局结束后可以解锁", "Unlock after this run ends");
            Add("status.PrerequisiteLocked", "需要先解锁前置节点", "Unlock a prerequisite first");
            Add("status.InsufficientPoints", "成长积分不足", "Not enough growth points");
            Add("status.UnknownNode", "节点不存在", "Unknown node");
            Add("status.StaleConfirmation", "进度已变化，请重新确认", "Progress changed; confirm again");
            Add("effect.FX_A0", "乐句Perfect率达到{1}时，奖励基础收入的{0}。", "A phrase with Perfect rate at least {1} rewards {0} of base income.");
            Add("effect.FX_A1", "连续{5}个乐句达到A0条件，额外奖励基础收入的{0}；触发后重新计数。", "Every {5} consecutive A0-qualified phrases reward an extra {0} of base income; then reset the streak.");
            Add("effect.FX_B0", "完成有音符且未断连的乐句，奖励基础收入的{0}。", "Complete a nonempty unbroken phrase for {0} of base income.");
            Add("effect.FX_B1", "连续{5}个乐句达到B0条件，额外奖励基础收入的{0}；触发后重新计数。", "Every {5} consecutive B0-qualified phrases reward an extra {0} of base income; then reset the streak.");
            Add("effect.FX_C0", "完成有效乐句，奖励基础收入的{0}。", "Complete a valid phrase for {0} of base income.");
            Add("effect.FX_C1", "成功演奏后，获得开曲现金的{0}；最多为基础收入的{2}。", "On song success, earn {0} of opening cash, capped at {2} of base income.");
            Add("effect.FX_D0", "演奏直接奖励与补偿奖励增加{0}。", "Direct performance rewards and compensation gain {0}.");
            Add("effect.FX_D1", "购买价格减免{0}；与其他折扣取最大值。", "Reduce purchase prices by {0}; use the best available discount.");
            Add("effect.FX_E0", "每局获得{4}次整批商店免费刷新。", "Gain {4} free whole-shop refreshes per run.");
            Add("effect.FX_E1", "E0提供的每局刷新次数增加{4}次。", "Add {4} to the refresh budget granted by E0.");
            Add("effect.FX_F0", "付费路线费用减免{0}；与其他优惠取最大值。", "Reduce paid route fees by {0}; use the best discount.");
            Add("effect.FX_F1", "将F0路费减免提高至{0}，覆盖原数值。", "Upgrade F0 route discount to {0}, replacing its previous value.");
            Add("effect.FX_G0", "成功演奏后的演奏收益保底为基础收入的{0}，仍受总上限约束。", "Successful performance income has a floor of {0} of base income, subject to the overall cap.");
            Add("effect.FX_G1", "将G0保底提高至基础收入的{0}，覆盖原数值。", "Upgrade G0 floor to {0} of base income, replacing its previous value.");
            Add("effect.FX_I0", "整曲Perfect率达到{1}且成功完成，奖励基础收入的{0}。", "Succeed with whole-song Perfect rate at least {1} to earn {0} of base income.");
            Add("effect.FX_I1", "将I0达标奖励提高至基础收入的{0}，覆盖原数值。", "Upgrade I0 reward to {0} of base income, replacing its previous value.");
            Add("effect.FX_J0", "成功完成付费挑战，额外获得基础收入的{0}。", "Complete a paid challenge for an extra {0} of base income.");
            Add("effect.FX_J1", "将J0挑战奖励提高至基础收入的{0}，覆盖原数值。", "Upgrade J0 challenge reward to {0} of base income, replacing its previous value.");
            Add("effect.FX_K0", "A0、B0、C0的原始奖励增加{0}，不强化其他来源。", "Increase raw A0, B0 and C0 rewards by {0}; other sources are unaffected.");
            Add("effect.FX_K1", "每局可选{3}次购买减免{0}；成功购买才消耗，与其他折扣取最大值。", "Optionally use a {0} purchase discount {3} time(s) per run. Consume only on successful purchase; best discount wins.");
            Add("mapScrollHint", "上下拖动 / 滚轮浏览 · 路线向下推进", "Drag or scroll vertically · Progress downward");
            Add("mapFocus", "回到当前位置", "Find current room");
            Add("tapToStart", "点击开始演奏", "Tap to start");
            Add("tapToShop", "点击进入商店", "Tap to open shop");
            Add("tapToLeave", "点击离开", "Tap to leave");
            Add("mapVisited", "已通过", "Visited");
            Add("mapAvailable", "可前往", "Available");
            Add("effect.FX_K2", "成功完成付费挑战后，返还实际支付路费的{0}。", "After a successful paid challenge, refund {0} of the route fee actually paid.");

            // 主菜单四按钮文案：与语言 JSON 同域（menu.*），用回退注册（工程铁律：AI 不写语言 JSON）
            Loc.AddFallback("menu.newGame", "新游戏", "New game");
            Loc.AddFallback("menu.saveSelect", "选择存档", "Select profile");
            Loc.AddFallback("menu.settings", "选项设置", "Settings");
            Loc.AddFallback("menu.newGameConfirm",
                "确认开始新游戏？\n将覆盖默认档案的进度：\n成长积分、已解锁天赋与局内进度\n都会被清空，且无法恢复。",
                "Start a new game?\nThis overwrites the default profile:\ngrowth points, unlocked talents and run progress\nwill be cleared and cannot be recovered.");

            // 选项设置页文案（settings.* 域，回退注册）
            Loc.AddFallback("settings.title", "选项设置", "Settings");
            Loc.AddFallback("settings.language", "语言", "Language");
            Loc.AddFallback("settings.openCalibration", "打开校准", "Open calibration");
            Loc.AddFallback("settings.fallSpeed", "下落速度", "Fall speed");
            Loc.AddFallback("settings.sfxVolume", "音效音量", "SFX volume");
            Loc.AddFallback("settings.hitEffect", "按键特效", "Hit effects");
            Loc.AddFallback("settings.hitEffectOn", "开", "On");
            Loc.AddFallback("settings.hitEffectOff", "关", "Off");
            Loc.AddFallback("settings.close", "关闭", "Close");

            // 存档选择面板文案（saveSelect.* 域，回退注册）
            Loc.AddFallback("saveSelect.title", "选择存档", "Select profile");
            Loc.AddFallback("saveSelect.name", "输入存档名称（最多32字）", "Profile name (up to 32 characters)");
            Loc.AddFallback("saveSelect.create", "新建存档", "Create profile");
            Loc.AddFallback("saveSelect.enterGame", "进入游戏", "Enter game");
            Loc.AddFallback("saveSelect.talents", "天赋", "Talents");
            Loc.AddFallback("saveSelect.close", "关闭", "Close");

            // 天赋面板文案（talentPanel.* 域，回退注册）
            Loc.AddFallback("talentPanel.title", "永久天赋", "Permanent talents");
            Loc.AddFallback("talentPanel.close", "✕", "✕");

            // 商店弹窗文案（shopPanel.* 域，回退注册）
            Loc.AddFallback("shopPanel.title", "商店", "Shop");
            Loc.AddFallback("shopPanel.close", "✕", "✕");

            // 装备背包面板文案（equipmentPanel.* 域，回退注册）
            Loc.AddFallback("equipmentPanel.title", "装备背包", "Equipment");
            Loc.AddFallback("equipmentPanel.close", "✕", "✕");
            Loc.AddFallback("equipmentPanel.empty", "尚未持有装备。\n可通过商店购买或在关卡中获得。", "No equipment yet.\nAcquire some via the shop or stage drops.");
            Loc.AddFallback("equipmentPanel.hint", "点击装备图标查看效果说明", "Select equipment to view its effect.");
            Loc.AddFallback("equipmentPanel.price", "价格 {0}", "Price {0}");
        }
        private static void Add(string key, string chinese, string english) => Loc.AddFallback("portfolio." + key, chinese, english);
    }
}
