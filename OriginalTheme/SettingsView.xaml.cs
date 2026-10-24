namespace MyClinic.OriginalTheme;
public partial class SettingsView : System.Windows.Controls.UserControl
{
    public SettingsView() { InitializeComponent(); }
    public SettingsView(System.Func<MyClinic.AppDbContext> createContext) : this()
    { Workspace = new MyClinic.SettingsView(createContext); Content = Workspace; }

}
