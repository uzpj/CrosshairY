namespace CrosshairY;

public class AppState
{
    public string ProofKey  { get; set; } = "h";
    public string CycleKey  { get; set; } = "";
    public string ToggleKey { get; set; } = "";
    public string FollowKey { get; set; } = "";

    public bool   UpdateNotifications { get; set; } = true;
    public string LastUpdateCheck     { get; set; } = "";

    public bool CaptureHidden { get; set; } = false;

    public int MonitorIndex { get; set; } = 0;

    public string CrTemplate    { get; set; } = "";
    public string CrColor       { get; set; } = "#ffffff";
    public bool   CrOutline     { get; set; } = false;
    public int    CrOutlineSize { get; set; } = 1;
    public int    CrSize        { get; set; } = 100;
    public int    CrOpacity     { get; set; } = 100;
    public int    CrGap         { get; set; } = 3;
    public int    CrOffsetX     { get; set; } = 0;
    public int    CrOffsetY     { get; set; } = 0;
    public bool   CrFollowCursor { get; set; } = false;

    public List<string> CrCustomPixels { get; set; } = new();
    public int CrBuilderSize { get; set; } = 15;

    public string CrImagePath  { get; set; } = "";

    public bool AutoSwitchGames   { get; set; } = false;
    public bool AutoRevertProfile { get; set; } = false;
    public Dictionary<string, string> GameProfiles { get; set; } = new();
    public List<string> CustomGames { get; set; } = new();

    public List<CrosshairSlot> CrSlots { get; set; } = new();
}

public class CrosshairSlot
{
    public string Key         { get; set; } = "";
    public string Template    { get; set; } = "";
    public string Color       { get; set; } = "#ffffff";
    public bool   Outline     { get; set; } = false;
    public int    OutlineSize { get; set; } = 1;
    public int    Size        { get; set; } = 100;
    public int    Opacity     { get; set; } = 100;
    public int    Gap         { get; set; } = 3;
    public int    OffsetX     { get; set; } = 0;
    public int    OffsetY     { get; set; } = 0;
    public List<string> CustomPixels { get; set; } = new();
    public int    BuilderSize  { get; set; } = 15;
    public string ImagePath   { get; set; } = "";
}
