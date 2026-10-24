namespace MyClinic.OriginalTheme;
public partial class ShortagesView : System.Windows.Controls.UserControl
{
    public ShortagesView() { InitializeComponent(); }
    public ShortagesView(System.Func<MyClinic.AppDbContext> createContext) : this()
    { Workspace = new MyClinic.ShortagesView(createContext); Content = Workspace; }

}
