namespace MyClinic.OriginalTheme;
public partial class LabView : System.Windows.Controls.UserControl
{
    public LabView() { InitializeComponent(); }
    public LabView(System.Func<MyClinic.AppDbContext> createContext) : this()
    { Workspace = new MyClinic.LabView(createContext); Content = Workspace; }
    public void ReleaseResources() => Workspace.ReleaseResources(); public void RefreshDateForAppTimeChange(System.DateTime previous) => Workspace.RefreshDateForAppTimeChange(previous);
}
