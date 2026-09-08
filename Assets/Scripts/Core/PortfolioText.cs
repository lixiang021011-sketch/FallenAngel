namespace FallenAngel.Core
{
    /// <summary>成长验证入口的本地化回退。正式语言资产可用同名key覆盖。</summary>
    public static class PortfolioText
    {
        public static void Register()
        {
            Add("mapTitle", "FALLEN ANGEL  /  旅程地图", "FALLEN ANGEL / JOURNEY MAP");
            Add("mapIntro", "选择房间 · 免费与付费分支 · 汇合抵达终点", "Choose rooms · Free and paid branches · Converge at the finale");
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
            Add("node.FINAL", "终点战斗", "Final battle");
            Add("node.SHOP", "商店", "Shop");
            Add("node.EMPTY", "空占位房", "Empty room");
            Add("room.SHOP", "已进入商店。交易尚未接入，可离开继续前进。", "Shop reached. Trading is not connected yet; leave to continue.");
            Add("room.EMPTY", "这是一间空房，目前没有事件。", "An empty room. No event here yet.");
            Add("mapLegend", "亮色：可进入 · 绿色：当前位置 · 橙色连线：付费\n每个分叉都有免费出口。战斗仍共用占位鼓谱。", "Bright: reachable · Green: current · Orange edge: paid\nEvery fork has a free exit. Battles share the placeholder chart.");
            Add("legacyRun", "此存档仍在旧版线性局中；结束本局后，新一局使用分叉地图。", "This profile has a legacy linear run. The next run uses the branching map.");
            Add("entry", "成长流程验证", "Growth playtest");
            Add("title", "FALLEN ANGEL  /  永久成长", "FALLEN ANGEL  /  PERMANENT GROWTH");
            Add("intro", "免费路线 · 三次演奏 · 共用占位鼓谱", "Free route · three performances · shared placeholder chart");
            Add("profiles", "存档列表", "Profiles");
            Add("create", "新建存档", "Create profile");
            Add("name", "输入存档名称（最多32字）", "Profile name (up to 32 characters)");
            Add("defaultProfileName", "默认档案", "Default profile");
            Add("empty", "每个存档独立保存成长积分和永久天赋。", "Each profile has independent growth points and permanent talents.");
            Add("back", "返回主菜单", "Main menu");
            Add("balance", "{0}  ·  成长积分 {1}", "{0}  ·  Growth points {1}");
            Add("equipment", "装备 {0}/{1}", "Equipment {0}/{1}");
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
            Add("effectPending", "当前验证永久成长流程；天赋收益效果尚未接入演奏。", "This build tests permanent growth. Talent reward effects are not yet applied to performances.");
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
        }
        private static void Add(string key, string chinese, string english) => Loc.AddFallback("portfolio." + key, chinese, english);
    }
}
