using System.ComponentModel;
using Emby.Web.GenericEdit;

namespace ArchiveMediaDrive.Emby;

public sealed class PluginOptions : EditableOptionsBase
{
    public override string EditorTitle => "ArchiveMediaDrive";
    public override string EditorDescription => "Use Internet Archive as an Emby media source.";

    [DisplayName("Channel enabled")]
    [Description("Browse Internet Archive items through the Emby channel interface.")]
    public bool ChannelEnabled { get; set; } = true;

    [DisplayName("Managed Library enabled")]
    [Description("Mount Internet Archive content as a read-only Emby library via rclone. Requires FUSE, WinFsp, or macOS mount support.")]
    public bool ManagedLibraryEnabled { get; set; }

    [DisplayName("Managed Library name")]
    [Description("Display name for the managed library folder.")]
    public string ManagedLibraryName { get; set; } = "Internet Archive";

    [DisplayName("Sources")]
    [Description("The Internet Archive sources to browse and mount.")]
    public EditableObjectCollection Sources { get; set; } = new EditableObjectCollection();
}
