using System.Windows;
using System.Windows.Controls;
using MyQQMusic.Models;
using MyQQMusic.ViewModels;

namespace MyQQMusic.Views;

public partial class SettingsView : UserControl
{
    SettingsViewModel? _vm;

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => _vm = DataContext as SettingsViewModel;
    }

    /// <summary>背景选项：Custom 走选图流程，其余直接切换</summary>
    void BackgroundChip_Click(object sender, RoutedEventArgs e)
    {
        if (_vm == null || sender is not Button btn) return;
        if (btn.DataContext is not OptionItem item) return;

        if (item.Key == "Custom")
            _vm.ChooseBackgroundImageCommand.Execute(null);
        else if (item.Key == "CustomColor")
            _vm.ChooseBackgroundColorCommand.Execute(null);
        else
            _vm.SetBackgroundCommand.Execute(item.Key);
    }
}
