namespace RobloxAccountManager.Core.Services;

/// <summary>移植 MultiRoblox-RAM 功能時新增的介面字串（中文）。</summary>
public static partial class Loc
{
    private static readonly Dictionary<string, string> ZhExtra = new()
    {
        ["Open"] = "開啟",

        // 主視窗
        ["Enter the password that protects your accounts:"] = "請輸入保護帳號資料的密碼：",
        ["Wrong password, try again:"] = "密碼錯誤，請再試一次：",
        ["View"] = "檢視", ["List"] = "清單", ["Tiles"] = "磚塊",
        ["Add Account by Logging In..."] = "登入並新增帳號...",
        ["Import Encrypted accounts.dat (V2 / RAM)..."] = "匯入加密的 accounts.dat（V2 / RAM）...",
        ["Edit Account..."] = "編輯帳號...", ["Account Tools..."] = "帳號工具...", ["Open in Browser"] = "在瀏覽器開啟",
        ["Kill Selected Instances"] = "結束選取的實例", ["Kill Checked Instances"] = "結束已勾選的實例",
        ["Trim Memory (Selected)"] = "釋放記憶體（選取）",
        ["Join Private Server Link..."] = "加入私人伺服器連結...", ["Server List..."] = "伺服器清單...",
        ["Game Charts..."] = "遊戲排行榜...", ["Tracking..."] = "追蹤（截圖）...", ["Help"] = "說明",
        ["Kill Checked"] = "結束已勾選", ["Status"] = "狀態", ["Target"] = "目標",
        ["Kill Instance"] = "結束實例", ["Trim Memory"] = "釋放記憶體", ["Process Priority"] = "處理程序優先權",
        ["All accounts cleared"] = "已清除所有帳號",
        ["No target: enter a Place ID or set one on the account"] = "沒有目標：請輸入 Place ID，或在帳號上設定目標",
        ["Paste a private server link (…/games/ID/…?privateServerLinkCode=… or roblox.com/share?code=…&type=Server):"] =
            "貼上私人伺服器連結（…/games/ID/…?privateServerLinkCode=… 或 roblox.com/share?code=…&type=Server）：",
        ["Not a private server link"] = "這不是私人伺服器連結",
        ["This is not a Roblox Account Manager encrypted file."] = "這不是 Roblox Account Manager 的加密檔。",
        ["Password of the file:"] = "檔案密碼：",
        ["Could not read the account after login"] = "登入後無法讀取帳號資料",
        ["HelpText"] =
            "• 勾選帳號後按「啟動已勾選」依序啟動；雙擊帳號可單獨加入。\n" +
            "• 右鍵「編輯帳號」可替每個帳號設定專屬目標（Place ID / 遊戲網址 / Job ID 連結 / 私人伺服器連結 / 追蹤某位玩家）。\n" +
            "• 開啟多開（Multi-Roblox）後才能同時執行多個 Roblox。\n" +
            "• 「混音台」可調整每個實例的音量、記憶體、CPU、優先權與視窗大小。\n" +
            "• 設定 → 服務：可啟用本機 Web API（預設 7963）與 Nexus（預設 7964），均只監聽 localhost。\n" +
            "• 設定 → 資料與隱私：可改用自訂密碼加密帳號檔；忘記密碼將無法還原。",

        // 混音台
        ["Volume"] = "音量", ["Priority"] = "優先權", ["Volume:"] = "音量：",
        ["Set Selected"] = "套用到選取", ["Set All"] = "套用到全部", ["Mute"] = "靜音", ["Unmute"] = "取消靜音",
        ["Trim Memory (All)"] = "釋放記憶體（全部）", ["Priority:"] = "優先權：", ["Kill Selected"] = "結束選取",
        ["Renderer:"] = "渲染器：", ["Quality level:"] = "畫質等級：", ["Apply Graphics Settings"] = "套用圖形設定",
        ["Written to ClientAppSettings.json; takes effect on the next launch. Quality 0 = do not override."] =
            "寫入 ClientAppSettings.json，下次啟動才生效。畫質 0 = 不覆寫。",
        ["No audio session yet"] = "尚無音訊工作階段",
        ["Failed to write ClientAppSettings.json (is Roblox installed?)"] = "無法寫入 ClientAppSettings.json（Roblox 已安裝嗎？）",

        // 帳號編輯
        ["Edit Account"] = "編輯帳號",
        ["Launch target for this account (Place ID, game URL, Job ID link, or private server link; blank = use the main window's Place/Job):"] =
            "此帳號的啟動目標（Place ID、遊戲網址、Job ID 連結或私人伺服器連結；留空 = 使用主視窗的 Place/Job）：",
        ["Follow a player instead (username; overrides the target above):"] = "改為追蹤某位玩家（用戶名；會覆蓋上面的目標）：",
        ["Look up"] = "查詢", ["Email password"] = "信箱密碼",
        ["Account password (used by Account Tools)"] = "帳號密碼（帳號工具會用到）",
        ["Custom fields (key=value per line)"] = "自訂欄位（每行 key=value）",
        ["Not recognised - this target will be ignored."] = "無法辨識 - 這個目標會被忽略。",
        ["User not found."] = "找不到該用戶。",

        // 帳號工具
        ["Account Tools"] = "帳號工具", ["Selected:"] = "已選取：", ["PIN"] = "PIN 碼",
        ["Account PIN (4 digits):"] = "帳號 PIN（4 位數）：", ["Unlock"] = "解鎖", ["Unlock PIN"] = "解鎖 PIN",
        ["Privacy"] = "隱私", ["Who can follow me:"] = "誰可以追蹤我：", ["Apply"] = "套用", ["Follow privacy"] = "追蹤隱私",
        ["Password"] = "密碼", ["Current:"] = "目前：", ["New:"] = "新密碼：", ["Change"] = "變更",
        ["Password must be at least 8 characters"] = "密碼至少 8 個字元", ["Change password"] = "變更密碼",
        ["Email"] = "電子郵件", ["Password:"] = "密碼：", ["New email:"] = "新信箱：", ["Change email"] = "變更信箱",
        ["Display name"] = "顯示名稱", ["Set"] = "設定", ["Sessions"] = "登入工作階段",
        ["Log out other sessions"] = "登出其他裝置",
        ["This signs the selected accounts out everywhere else and issues a new cookie. Continue?"] = "這會讓選取的帳號在其他地方全部登出，並產生新的 Cookie。要繼續嗎？",
        ["Blocking"] = "封鎖", ["Username:"] = "用戶名：", ["Block / Unblock"] = "封鎖 / 解除封鎖", ["Block"] = "封鎖",
        ["Unblock Everyone"] = "解除全部封鎖", ["Unblock every blocked user on the selected accounts?"] = "要解除選取帳號上所有被封鎖的用戶嗎？",
        ["Unblock everyone"] = "解除全部封鎖", ["Friends"] = "好友", ["Send Friend Request"] = "送出好友邀請", ["Friend request"] = "好友邀請",
        ["Find player"] = "尋找玩家", ["Find Server"] = "尋找伺服器",
        ["(Only the first selected account is used for this action)"] = "（此操作只會使用第一個選取的帳號）",
        ["Place ID / Job ID filled in the main window"] = "已填入主視窗的 Place ID / Job ID",

        // 排行榜 / 伺服器清單
        ["Game Charts"] = "遊戲排行榜", ["Refresh"] = "重新整理", ["Use This Game"] = "使用這個遊戲", ["Servers..."] = "伺服器...",
        ["Launch Checked Here"] = "用已勾選帳號啟動", ["Playing"] = "遊玩中", ["(All / Search)"] = "（全部 / 搜尋）",
        ["Charts are unavailable right now"] = "目前無法取得排行榜", ["lists"] = "個清單",
        ["Server List"] = "伺服器清單", ["Pages:"] = "頁數：", ["Join Selected Server"] = "加入選取的伺服器",
        ["Copy Job ID"] = "複製 Job ID", ["Players"] = "玩家數", ["servers"] = "個伺服器", ["Select a server first"] = "請先選一個伺服器",

        // 記錄
        ["Auto-scroll"] = "自動捲動", ["Logs"] = "記錄", ["Clear"] = "清除", ["Copy"] = "複製", ["Search (Ctrl+F):"] = "搜尋 (Ctrl+F)：",

        // Nexus
        ["username"] = "用戶名", ["Nexus is off. Enable it in Settings → Services."] = "Nexus 未啟用，請到 設定 → 服務 開啟。",
        ["Argument (chat text / kick reason / script / \"placeId [jobId]\"):"] = "參數（聊天內容 / 踢出原因 / 腳本 / \"placeId [jobId]\"）：",
        ["Send to Selected"] = "傳給選取者", ["Send to All"] = "傳給全部", ["Connected"] = "已連線",
        ["Nexus is not running."] = "Nexus 未在執行。", ["JoinServer needs a Place ID."] = "JoinServer 需要 Place ID。",

        // 設定
        ["General"] = "一般",
        ["Relaunch an account into its game if it disconnects or crashes"] = "帳號斷線或當機時自動重新進入遊戲",
        ["Watch Roblox processes"] = "監看 Roblox 處理程序", ["…close an instance whose memory stays below (MB):"] = "…關閉記憶體持續低於此值的實例（MB）：",
        ["Hide to the system tray when the window is closed"] = "關閉視窗時縮到系統匣",
        ["Roblox channel (blank = default):"] = "Roblox 頻道（留空 = 預設）：",
        ["Performance"] = "效能", ["Lower CPU priority (Below Normal) while 2 or more instances are running"] = "同時執行 2 個以上實例時降低 CPU 優先權（低於正常）",
        ["Block the crash handler (closes RobloxCrashHandler.exe)"] = "封鎖當機處理程式（結束 RobloxCrashHandler.exe）",
        ["Check for updates on startup"] = "啟動時檢查更新", ["Check for Updates..."] = "檢查更新...",
        ["This is a development build; updates are disabled."] = "這是開發版本，已停用自動更新。",
        ["Checking for updates…"] = "正在檢查更新…", ["You are on the latest version."] = "已經是最新版。",
        ["A new version is available:"] = "有新版本：", ["current"] = "目前",
        ["Update now? The app will restart; your accounts and settings are kept."] = "要現在更新嗎？程式會重新啟動，帳號與設定都會保留。",
        ["Downloading update…"] = "正在下載更新…", ["Update failed:"] = "更新失敗：",
        ["Automatically trim memory every (minutes):"] = "每隔幾分鐘自動釋放記憶體：", ["Trim memory now"] = "立即釋放記憶體",
        ["Force a rendering engine (applies to newly started instances):"] = "強制指定渲染引擎（只影響新啟動的實例）：",
        ["Graphics quality override 1-21 (0 = don't override):"] = "覆寫圖形品質 1-21（0 = 不覆寫）：",
        ["FPS cap, CPU / RAM limits, window size and volume are in the Mixer."] = "FPS 上限、CPU / RAM 限制、視窗大小與音量請到「混音台」設定。",
        ["Data & Privacy"] = "資料與隱私", ["Set a password..."] = "設定密碼...", ["Remove password (use Windows account)"] = "移除密碼（改用 Windows 帳號加密）",
        ["Accounts are encrypted on disk: with Windows DPAPI (tied to your Windows user) by default, or with AES-256-GCM using a password you choose. With a password set you must enter it every time the app starts."] =
            "帳號檔在硬碟上是加密的：預設用 Windows DPAPI（綁定你的 Windows 使用者），也可以用你自訂的密碼做 AES-256-GCM 加密。設定密碼後，每次啟動程式都必須輸入。",
        ["Clear all accounts..."] = "清除所有帳號...",
        ["Themes"] = "主題", ["Theme:"] = "主題：", ["Theme Editor..."] = "主題編輯器...",
        ["Sounds"] = "音效", ["Play a click sound on buttons and menus"] = "按下按鈕與選單時播放點擊音效",
        ["Custom sound (.wav, blank = built-in):"] = "自訂音效（.wav，留空 = 內建）：", ["Test"] = "測試",
        ["Services"] = "服務", ["Enable the local Web API (http://localhost:port)"] = "啟用本機 Web API（http://localhost:連接埠）",
        ["Port:"] = "連接埠：", ["Password (optional):"] = "密碼（選填）：",
        ["Enable Nexus (WebSocket control of in-game scripts)"] = "啟用 Nexus（以 WebSocket 控制遊戲內腳本）",
        ["Both services only listen on localhost. A password is checked in the 'password' header or query string."] = "兩個服務都只監聽 localhost。密碼會從 'password' 標頭或查詢字串檢查。",
        ["Encryption: custom password (AES-256-GCM)"] = "加密方式：自訂密碼（AES-256-GCM）",
        ["Encryption: Windows DPAPI (current Windows user)"] = "加密方式：Windows DPAPI（目前的 Windows 使用者）",
        ["New password (at least 6 characters). You will be asked for it on every start; if you forget it the accounts cannot be recovered."] =
            "新密碼（至少 6 個字元）。每次啟動都會要求輸入；忘記就無法還原帳號。",
        ["Password is too short."] = "密碼太短。", ["Type it again to confirm:"] = "請再輸入一次確認：", ["Passwords do not match."] = "兩次密碼不一致。",
        ["This permanently deletes ALL saved accounts and their cookies from this app. Continue?"] = "這會永久刪除此程式內所有已儲存的帳號與 Cookie。要繼續嗎？",
        ["Are you really sure? This cannot be undone."] = "真的確定嗎？此動作無法復原。",

        // 主題編輯器
        ["Theme Editor"] = "主題編輯器", ["Start from:"] = "以此為底：", ["Background image (optional):"] = "背景圖片（選填）：",
        ["Opacity:"] = "不透明度：", ["Theme name:"] = "主題名稱：", ["Save & Apply"] = "儲存並套用", ["Delete"] = "刪除",
        ["Invalid colour:"] = "無效的顏色：", ["Enter a theme name."] = "請輸入主題名稱。", ["That name is used by a built-in theme."] = "這個名稱已被內建主題使用。",
        ["Saved and applied."] = "已儲存並套用。", ["Only custom themes can be deleted."] = "只能刪除自訂主題。", ["Deleted."] = "已刪除。",

        // 追蹤
        ["Tracking"] = "追蹤",
        ["Take a screenshot of every running Roblox window on a timer and post it to a Discord webhook."] = "定時對每個執行中的 Roblox 視窗截圖，並傳到 Discord webhook。",
        ["Enable tracking"] = "啟用追蹤", ["Discord webhook URL:"] = "Discord webhook 網址：", ["Every (minutes):"] = "每幾分鐘：",
        ["Capture regions per account (one per line: x,y,w,h in pixels from the window's top-left; empty = whole window):"] =
            "每個帳號的截圖區域（每行一個：x,y,w,h，單位為像素，自視窗左上角算起；空白 = 整個視窗）：",
        ["Save"] = "儲存", ["Capture Now"] = "立即截圖",
        ["Saved, but these regions are invalid and will be skipped:"] = "已儲存，但下列區域格式無效，會被略過：",
        ["Saved. Enter a valid https webhook URL to start tracking."] = "已儲存。請輸入有效的 https webhook 網址才會開始追蹤。",
        ["Saved."] = "已儲存。", ["Enter a valid https webhook URL first."] = "請先輸入有效的 https webhook 網址。", ["Sent screenshots:"] = "已傳送截圖：",

        // 主視窗新版面
        ["Search accounts, aliases, groups…"] = "搜尋帳號、別名、群組…", ["Groups"] = "群組", ["Ungrouped"] = "未分組",
        ["Destination"] = "目的地", ["Job ID or join link (optional)"] = "Job ID 或加入連結（選填）",
        ["Running"] = "執行中", ["Roblox instances"] = "個 Roblox 實例", ["Join with Selected"] = "用選取的帳號加入",
        ["Account"] = "帳號", ["Looking up the game…"] = "正在查詢遊戲…", ["Game not found"] = "找不到這個遊戲",
        ["Choose a destination"] = "選擇目的地", ["playing now"] = "人正在玩",
        ["Check the Place ID, or paste a game / server link into the Job ID box."] = "請確認 Place ID，或把遊戲 / 伺服器連結貼到 Job ID 欄。",
        ["Enter a Place ID, or paste a game / server link into the Job ID box."] = "輸入 Place ID，或把遊戲 / 伺服器連結貼到 Job ID 欄。",

        ["Performance limits"] = "效能限制", ["Window"] = "視窗", ["Audio and process"] = "音訊與處理程序",
        ["Graphics"] = "圖形", ["Running instances"] = "執行中的實例",
        ["Minimize"] = "最小化", ["Maximize"] = "最大化", ["Restore"] = "還原", ["Close"] = "關閉",

        // 網頁控制面板
        ["Open Web Control Panel"] = "開啟網頁控制面板",
        ["The web control panel is off. Turn it on in Settings → Services."] = "網頁控制面板沒有開啟，請到 設定 → 服務 開啟。",
        ["Enable the web control panel and Web API"] = "啟用網頁控制面板與 Web API",
        ["Allow phones and other devices on this network (requires a password)"] = "允許手機等同一網路的裝置連線（需要設定密碼）",
        ["On this computer:"] = "這台電腦：", ["Other devices:"] = "其他裝置：",
        ["Set a password first; without one the panel only listens on this computer."] = "請先設定密碼；沒有密碼時面板只會開放給這台電腦。",
        ["The first time you allow other devices, Windows Firewall may ask — choose Private networks."] = "第一次允許其他裝置時，Windows 防火牆可能會詢問，請選「私人網路」。",
        ["Nexus only listens on this computer. Passwords are checked in the 'password' header or query string."] = "Nexus 只開放給這台電腦。密碼會從 'password' 標頭或查詢字串檢查。",

        // WebView2
        ["Log in to Roblox"] = "登入 Roblox", ["WebView2 is not available"] = "找不到 WebView2 執行環境",
        ["Reload page"] = "重新整理頁面",
        ["If the verification keeps failing: reload the page, or choose “Another login option → Quick Log In” and enter the code on a device where you're already signed in."] =
            "驗證一直失敗的話：先重新整理頁面；或改按「其他登入選項 → 快速登入」，在已登入 Roblox 的裝置上輸入畫面上的代碼。",
    };
}
