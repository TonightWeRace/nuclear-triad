namespace NuclearTriad.Rig
{
    /// <summary>
    /// Hints for BepInEx ConfigurationManager (F1), which finds this class by name and reads these
    /// fields: advanced settings stay hidden until its "Advanced settings" box is ticked, higher
    /// Order sorts first within a section, Category shows a setting under another heading than its
    /// cfg section, and a CustomDrawer draws the whole row in place of F1's own field.
    /// </summary>
#pragma warning disable 0649
    internal sealed class ConfigurationManagerAttributes
    {
        public bool? IsAdvanced;
        public int? Order;
        public string Category;
        public bool? ReadOnly;
        public System.Action<BepInEx.Configuration.ConfigEntryBase> CustomDrawer;
        public bool? HideSettingName;
        public bool? HideDefaultButton;
    }
#pragma warning restore 0649
}
