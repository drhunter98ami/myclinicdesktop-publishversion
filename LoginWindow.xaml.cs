using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MyClinic.Models; // Ensure this matches your Models namespace

namespace MyClinic
{
    public partial class LoginWindow : Window
    {
        // Segoe MDL2 icon codes
        private readonly string EyeOpenIcon = "\xE18B";   // Standard Eye
        private readonly string EyeClosedIcon = "\xED1A"; // Eye with a slash

        public LoginWindow()
        {
            InitializeComponent();
            CheckCredentialsStatus();
        }

        private void CheckCredentialsStatus()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    // Check if any users exist in the database
                    bool hasUsers = db.Users.Any();

                    if (hasUsers)
                    {
                        // Credentials exist, show login screen
                        pnlSetup.Visibility = Visibility.Collapsed;
                        pnlLogin.Visibility = Visibility.Visible;
                        
                        // Make Login button the default for the Enter key
                        BtnLoginButton.IsDefault = true;
                        BtnSetPasswordButton.IsDefault = false;
                    }
                    else
                    {
                        // No users found, show setup screen
                        pnlLogin.Visibility = Visibility.Collapsed;
                        pnlSetup.Visibility = Visibility.Visible;
                        
                        // Make Setup button the default for the Enter key
                        BtnSetPasswordButton.IsDefault = true;
                        BtnLoginButton.IsDefault = false;
                    }
                }
            }
            catch (Exception)
            {
                // Fallback in case of DB error during check (e.g., first run before DB creation)
                pnlLogin.Visibility = Visibility.Collapsed;
                pnlSetup.Visibility = Visibility.Visible;
                
                BtnSetPasswordButton.IsDefault = true;
                BtnLoginButton.IsDefault = false;
            }
        }

        // ================= SETUP LOGIC =================
        private void BtnSetPassword_Click(object sender, RoutedEventArgs e)
        {
            string newUsername = txtSetupUsername.Text.Trim();
            string newPass = txtSetupPass.Visibility == Visibility.Visible ? txtSetupPass.Password : txtSetupPassVis.Text;
            string confirmPass = txtSetupConfirm.Visibility == Visibility.Visible ? txtSetupConfirm.Password : txtSetupConfirmVis.Text;

            if (string.IsNullOrEmpty(newUsername) || string.IsNullOrEmpty(newPass))
            {
                ShowError("الرجاء إدخال اسم المستخدم وكلمة المرور.");
                return;
            }

            if (newPass != confirmPass)
            {
                ShowError("كلمات المرور غير متطابقة.");
                return;
            }

            try
            {
                using (var db = new AppDbContext())
                {
                    // Hash the password before saving it
                    string hashedPassword = BCrypt.Net.BCrypt.HashPassword(newPass);

                    // Save new user to the database
                    var newUser = new User 
                    { 
                        Username = newUsername, 
                        // Note: Ensure your User.cs model property is named PasswordHash
                        PasswordHash = hashedPassword 
                    };
                    
                    db.Users.Add(newUser);
                    db.SaveChanges(); // Commits to SQLite
                }

                txtError.Visibility = Visibility.Collapsed;
                pnlSetup.Visibility = Visibility.Collapsed;
                pnlLogin.Visibility = Visibility.Visible;
                
                // Swap default Enter key behavior to the login button
                BtnLoginButton.IsDefault = true;
                BtnSetPasswordButton.IsDefault = false;
                
                // Pre-fill username for convenience
                txtLoginUsername.Text = newUsername; 
                
                // Set focus to the password box
                txtLoginPass.Focus();
            }
            catch (Exception ex)
            {
                ShowError($"حدث خطأ أثناء حفظ البيانات: {ex.Message}");
            }
        }

        // ================= LOGIN LOGIC =================
        private void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            string username = txtLoginUsername.Text.Trim();
            string password = txtLoginPass.Visibility == Visibility.Visible ? txtLoginPass.Password : txtLoginPassVis.Text;

            try
            {
                using (var db = new AppDbContext())
                {
                    // 1. Find the user by Username ONLY
                    var user = db.Users.FirstOrDefault(u => u.Username == username);

                    // 2. If user exists, verify the typed password against the saved hash
                    if (user != null && BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
                    {
                        txtError.Visibility = Visibility.Collapsed;
                        LoginSessionStore.MarkSuccessfulLogin(user.Username);

                        MainWindow mainAppWindow = new MainWindow();
                        Application.Current.MainWindow = mainAppWindow;
                        mainAppWindow.Show();
                        this.Close();
                    }
                    else
                    {
                        ShowError("اسم المستخدم أو كلمة المرور غير صحيحة.");
                        txtLoginPass.Clear();
                        txtLoginPassVis.Clear();
                        txtLoginPass.Focus();
                    }
                }
            }
            catch (Exception ex)
            {
                ShowError($"حدث خطأ أثناء الاتصال بقاعدة البيانات: {ex.Message}");
            }
        }

        private void ShowError(string message)
        {
            txtError.Text = message;
            txtError.Visibility = Visibility.Visible;
        }

        // ================= PASSWORD VISIBILITY TOGGLES =================
        private void BtnSetupPassEye_Click(object sender, RoutedEventArgs e)
        {
            TogglePasswordVisibility(txtSetupPass, txtSetupPassVis, btnSetupPassEye);
        }

        private void BtnSetupConfirmEye_Click(object sender, RoutedEventArgs e)
        {
            TogglePasswordVisibility(txtSetupConfirm, txtSetupConfirmVis, btnSetupConfirmEye);
        }

        private void BtnLoginPassEye_Click(object sender, RoutedEventArgs e)
        {
            TogglePasswordVisibility(txtLoginPass, txtLoginPassVis, btnLoginPassEye);
        }

        private void TogglePasswordVisibility(PasswordBox pBox, TextBox tBox, Button btn)
        {
            if (pBox.Visibility == Visibility.Visible)
            {
                // Switch to visible text
                tBox.Text = pBox.Password;
                pBox.Visibility = Visibility.Collapsed;
                tBox.Visibility = Visibility.Visible;
                btn.Content = EyeClosedIcon; 
            }
            else
            {
                // Switch back to masked dots
                pBox.Password = tBox.Text;
                tBox.Visibility = Visibility.Collapsed;
                pBox.Visibility = Visibility.Visible;
                btn.Content = EyeOpenIcon; 
            }
        }
    }
}
