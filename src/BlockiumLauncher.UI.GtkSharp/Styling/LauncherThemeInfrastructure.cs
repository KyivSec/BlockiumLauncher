using Gdk;
using Gtk;
using BlockiumLauncher.UI.GtkSharp.Services;

namespace BlockiumLauncher.UI.GtkSharp.Styling;

internal sealed record LauncherThemePalette(
    string WindowBackground,
    string TopbarBackground,
    string TopbarBorder,
    string SidebarBackground,
    string SidebarBorder,
    string ContentBackground,
    string ContentBorder,
    string PrimaryText,
    string SecondaryText,
    string StatusText,
    string ToolbarButtonBackground,
    string ToolbarButtonHover,
    string ToolbarBorder,
    string ActionButtonBackground,
    string ActionButtonHover,
    string ActionBorder,
    string PrimaryButton,
    string PrimaryButtonHover,
    string DangerBackground,
    string DangerHover,
    string DangerText,
    string DangerBorder,
    string RowBackground,
    string RowHover,
    string RowSelected,
    string SearchBackground,
    string SearchBorder,
    string PopoverBackground,
    string PopoverBorder,
    string SectionBackground,
    string SectionMutedBackground,
    string Accent);

internal static class LauncherThemePaletteFactory
{
    public static LauncherThemePalette Create(bool isDarkTheme)
    {
        return isDarkTheme
            ? new LauncherThemePalette(
                WindowBackground: "#0f141a",
                TopbarBackground: "#18212b",
                TopbarBorder: "#2a3744",
                SidebarBackground: "#14202a",
                SidebarBorder: "#263442",
                ContentBackground: "#111820",
                ContentBorder: "#1e2a35",
                PrimaryText: "#edf3f8",
                SecondaryText: "#9cb0c2",
                StatusText: "#73d49c",
                ToolbarButtonBackground: "#202b36",
                ToolbarButtonHover: "#273544",
                ToolbarBorder: "#334251",
                ActionButtonBackground: "#1d2833",
                ActionButtonHover: "#24313d",
                ActionBorder: "#31404f",
                PrimaryButton: "#2e7be6",
                PrimaryButtonHover: "#4f95ef",
                DangerBackground: "#352326",
                DangerHover: "#422b2f",
                DangerText: "#f2a9a9",
                DangerBorder: "#5a373d",
                RowBackground: "#111820",
                RowHover: "#18222d",
                RowSelected: "#1a2734",
                SearchBackground: "#17212b",
                SearchBorder: "#2e3b48",
                PopoverBackground: "#1a232d",
                PopoverBorder: "#334251",
                SectionBackground: "#16202a",
                SectionMutedBackground: "#1a2530",
                Accent: "#2e7be6")
            : new LauncherThemePalette(
                WindowBackground: "#eef3f8",
                TopbarBackground: "#e6edf5",
                TopbarBorder: "#cbd6e1",
                SidebarBackground: "#dbe6f0",
                SidebarBorder: "#c8d4de",
                ContentBackground: "#ffffff",
                ContentBorder: "#e1e8ef",
                PrimaryText: "#22303c",
                SecondaryText: "#617386",
                StatusText: "#2a7a53",
                ToolbarButtonBackground: "#f3f7fb",
                ToolbarButtonHover: "#fbfdff",
                ToolbarBorder: "#cad5df",
                ActionButtonBackground: "#f7f9fc",
                ActionButtonHover: "#ffffff",
                ActionBorder: "#c9d3dc",
                PrimaryButton: "#2e7be6",
                PrimaryButtonHover: "#276fd1",
                DangerBackground: "#f8f1f1",
                DangerHover: "#fcecec",
                DangerText: "#9f4343",
                DangerBorder: "#d8c6c6",
                RowBackground: "#ffffff",
                RowHover: "#f5f9fd",
                RowSelected: "#edf4fd",
                SearchBackground: "#f7f9fc",
                SearchBorder: "#d2dce5",
                PopoverBackground: "#ffffff",
                PopoverBorder: "#d2dce5",
                SectionBackground: "#ffffff",
                SectionMutedBackground: "#f7f9fc",
                Accent: "#2e7be6");
    }
}

public sealed class LauncherGtkThemeService
{
    private readonly CssProvider CssProvider = new();
    private readonly LauncherUiPreferencesService UiPreferences;
    private bool IsInitialized;

    public LauncherGtkThemeService(LauncherUiPreferencesService uiPreferences)
    {
        UiPreferences = uiPreferences ?? throw new ArgumentNullException(nameof(uiPreferences));
    }

    public void Initialize()
    {
        if (IsInitialized)
        {
            return;
        }

        StyleContext.AddProviderForScreen(Screen.Default, CssProvider, StyleProviderPriority.Application);
        UiPreferences.Changed += HandlePreferencesChanged;
        IsInitialized = true;
        Reload();
    }

    private void HandlePreferencesChanged(object? sender, EventArgs e)
    {
        Gtk.Application.Invoke((_, _) => Reload());
    }

    private void Reload()
    {
        var palette = LauncherThemePaletteFactory.Create(UiPreferences.IsDarkTheme);
        CssProvider.LoadFromData(BuildCss(palette));
    }

    private static string BuildCss(LauncherThemePalette palette)
    {
        return $$"""
            window {
                background: {{palette.WindowBackground}};
                color: {{palette.PrimaryText}};
                font-family: {{LauncherFontBootstrapper.PreferredBodyFontFamily}};
            }

            label,
            button,
            entry,
            combobox,
            textview,
            treeview,
            checkbutton,
            radiobutton {
                color: {{palette.PrimaryText}};
                font-family: {{LauncherFontBootstrapper.PreferredBodyFontFamily}};
                font-weight: 400;
            }

            dialog,
            messagedialog,
            dialog .dialog-vbox,
            dialog .content-area,
            dialog box,
            viewport {
                background: {{palette.WindowBackground}};
                color: {{palette.PrimaryText}};
            }

            dialog .dialog-action-area,
            dialog actionbar {
                background: {{palette.TopbarBackground}};
                border-top: 1px solid {{palette.TopbarBorder}};
            }

            button,
            entry,
            combobox,
            combobox box.linked button {
                box-shadow: none;
                text-shadow: none;
                border-radius: 0;
            }

            entry,
            combobox,
            combobox box.linked button {
                background: {{palette.SearchBackground}};
                color: {{palette.PrimaryText}};
                border: 1px solid {{palette.SearchBorder}};
                min-height: 22px;
                padding: 8px 10px;
            }

            entry placeholder {
                color: {{palette.SecondaryText}};
            }

            combobox.app-combo-field box.linked button,
            .app-combo-field box.linked button {
                background: {{palette.SearchBackground}};
                color: {{palette.PrimaryText}};
                border: 1px solid {{palette.SearchBorder}};
                min-height: 22px;
                padding: 8px 10px;
            }

            entry selection,
            textview text selection {
                background: {{palette.Accent}};
                color: #ffffff;
            }

            entry:focus,
            combobox:focus,
            combobox box.linked button:focus,
            .app-field:focus,
            .app-combo-field:focus,
            .app-combo-field box.linked button:focus {
                border-color: {{palette.Accent}};
            }

            entry:disabled,
            entry.app-field-readonly,
            .app-field-readonly,
            .app-field-readonly box.linked button {
                background: {{palette.SectionMutedBackground}};
                color: {{palette.SecondaryText}};
                border-color: {{palette.ContentBorder}};
            }

            entry.app-search-field {
                min-height: 18px;
                padding: 6px 10px;
            }

            .app-field-label {
                color: {{palette.PrimaryText}};
                font-weight: 600;
                font-family: {{LauncherFontBootstrapper.PreferredTitleFontFamily}};
                font-size: 13px;
            }

            .app-field-help {
                color: {{palette.SecondaryText}};
                font-size: 12px;
            }

            .app-field-block {
                background: transparent;
            }

            headerbar.topbar-shell,
            .topbar-shell {
                background: {{palette.TopbarBackground}};
                background-image: none;
                border-bottom: 1px solid {{palette.TopbarBorder}};
                border-top: 0;
                border-left: 0;
                border-right: 0;
                box-shadow: none;
            }

            headerbar.topbar-shell {
                min-height: 0;
                padding: 8px 14px;
            }

            headerbar.topbar-shell:backdrop {
                background: {{palette.TopbarBackground}};
                background-image: none;
            }

            .topbar-content,
            .toolbar-group,
            .browser-controls,
            .sidebar-actions,
            .popover-content {
                background: transparent;
            }

            .sidebar-shell {
                background: {{palette.SidebarBackground}};
                border-right: 1px solid {{palette.SidebarBorder}};
            }

            .content-shell {
                background: {{palette.ContentBackground}};
            }

            .sidebar-summary {
                border-bottom: 1px solid {{palette.SidebarBorder}};
                padding-bottom: 16px;
            }

            .instance-name,
            .settings-title,
            .settings-page-title,
            .popover-title {
                color: {{palette.PrimaryText}};
                font-family: {{LauncherFontBootstrapper.PreferredTitleFontFamily}};
                font-weight: 600;
            }

            .instance-name,
            .settings-title {
                font-size: 16px;
            }

            .secondary-text,
            .settings-subtitle,
            .settings-caption,
            .settings-help,
            .filter-section-title,
            .empty-row-text {
                color: {{palette.SecondaryText}};
                font-family: {{LauncherFontBootstrapper.PreferredBodyFontFamily}};
                font-weight: 400;
                font-size: 13px;
            }

            .settings-section-title {
                color: {{palette.PrimaryText}};
                font-family: {{LauncherFontBootstrapper.PreferredTitleFontFamily}};
                font-weight: 600;
                font-size: 14px;
            }

            .status-text,
            .settings-valid {
                color: {{palette.StatusText}};
                font-weight: 700;
                font-size: 13px;
            }

            .settings-invalid {
                color: {{palette.DangerText}};
                font-weight: 700;
                font-size: 13px;
            }

            .toolbar-button {
                background: {{palette.ToolbarButtonBackground}};
                color: {{palette.PrimaryText}};
                border: 1px solid {{palette.ToolbarBorder}};
                padding: 7px 12px;
            }

            .toolbar-button:hover,
            .square-icon-button:hover,
            .theme-toggle-button:hover {
                background: {{palette.ToolbarButtonHover}};
            }

            headerbar.topbar-shell button.titlebutton {
                color: {{palette.PrimaryText}};
                background: transparent;
                background-image: none;
                border: 0;
                box-shadow: none;
            }

            headerbar.topbar-shell button.titlebutton:hover,
            headerbar.topbar-shell button.titlebutton:active,
            headerbar.topbar-shell button.titlebutton:checked,
            headerbar.topbar-shell button.titlebutton:focus {
                color: {{palette.PrimaryText}};
                background: {{palette.ToolbarButtonHover}};
                background-image: none;
                border: 0;
                box-shadow: none;
            }

            headerbar.topbar-shell button.titlebutton:backdrop,
            headerbar.topbar-shell button.titlebutton:hover:backdrop {
                color: {{palette.PrimaryText}};
                background: transparent;
                background-image: none;
            }

            .toolbar-button-label {
                color: {{palette.PrimaryText}};
                font-weight: 600;
                font-family: {{LauncherFontBootstrapper.PreferredTitleFontFamily}};
            }

            .square-icon-button,
            .theme-toggle-button {
                background: {{palette.ToolbarButtonBackground}};
                color: {{palette.PrimaryText}};
                border: 1px solid {{palette.ToolbarBorder}};
                padding: 0;
            }

            .action-button,
            .popover-menu-button {
                background: {{palette.ActionButtonBackground}};
                color: {{palette.PrimaryText}};
                border: 1px solid {{palette.ActionBorder}};
                padding: 8px 14px;
            }

            .action-button:hover,
            .popover-menu-button:hover {
                background: {{palette.ActionButtonHover}};
            }

            .primary-button {
                background: {{palette.PrimaryButton}};
                color: #ffffff;
                border-color: {{palette.PrimaryButton}};
            }

            .primary-button,
            .primary-button label,
            .primary-button image,
            .primary-button:hover,
            .primary-button:hover label,
            .primary-button:hover image,
            .primary-button:active,
            .primary-button:active label,
            .primary-button:active image,
            .primary-button:checked,
            .primary-button:checked label,
            .primary-button:checked image,
            .primary-button:disabled,
            .primary-button:disabled label,
            .primary-button:disabled image {
                color: #ffffff;
            }

            .primary-button:hover,
            .primary-button:active,
            .primary-button:checked {
                background: {{palette.PrimaryButtonHover}};
            }

            .primary-button:disabled {
                background: shade({{palette.PrimaryButton}}, 0.88);
                border-color: shade({{palette.PrimaryButton}}, 0.88);
            }

            .danger-button {
                background: {{palette.DangerBackground}};
                color: {{palette.DangerText}};
                border-color: {{palette.DangerBorder}};
            }

            .danger-button:hover {
                background: {{palette.DangerHover}};
            }

            .search-shell {
                background: {{palette.SearchBackground}};
                border: 1px solid {{palette.SearchBorder}};
            }

            .search-entry {
                background: transparent;
                color: {{palette.PrimaryText}};
                border: 0;
                box-shadow: none;
                padding: 0;
            }

            .instance-scroller,
            .settings-page-scroller,
            .settings-java-scroller {
                border: 0;
                background: transparent;
            }

            .instance-list,
            .settings-nav-list,
            .settings-java-list,
            .accounts-list {
                background: {{palette.ContentBackground}};
                border: 0;
            }

            .instance-list row,
            .instance-row,
            .settings-java-list row,
            .accounts-list row {
                padding: 0;
                background: {{palette.RowBackground}};
                border-bottom: 1px solid {{palette.ContentBorder}};
            }

            .instance-list row:hover,
            .settings-java-list row:hover,
            .accounts-list row:hover {
                background: {{palette.RowHover}};
            }

            .instance-list row:selected,
            .settings-nav-list row:selected,
            .accounts-list row:selected {
                background: {{palette.RowSelected}};
            }

            .instance-row-body,
            .settings-java-row-body,
            .account-row-body {
                background: transparent;
            }

            .instance-row-accent,
            .settings-nav-accent {
                background: transparent;
            }

            .instance-list row:selected .instance-row-accent,
            .settings-nav-list row:selected .settings-nav-accent {
                background: {{palette.Accent}};
            }

            .instance-row-text,
            .account-row-text,
            .settings-nav-text,
            .settings-row-label,
            .settings-row-value,
            .popover-check {
                color: {{palette.PrimaryText}};
                font-weight: 400;
                font-size: 14px;
            }

            popover,
            popover.background {
                background: {{palette.PopoverBackground}};
                color: {{palette.PrimaryText}};
                border: 1px solid {{palette.PopoverBorder}};
            }

            .flat-inline-button {
                background: transparent;
                color: {{palette.Accent}};
                border: 0;
                padding: 4px 0;
            }

            .flat-inline-button:hover {
                background: transparent;
                color: {{palette.PrimaryButtonHover}};
            }

            .empty-row {
                background: {{palette.RowBackground}};
            }

            .settings-shell {
                background: {{palette.WindowBackground}};
            }

            .settings-header {
                background: {{palette.TopbarBackground}};
                border-bottom: 1px solid {{palette.TopbarBorder}};
            }

            .settings-body {
                background: transparent;
            }

            .settings-nav-shell {
                background: {{palette.SidebarBackground}};
                border-right: 1px solid {{palette.SidebarBorder}};
            }

            .accounts-sidebar-shell {
                background: {{palette.SidebarBackground}};
                border-left: 1px solid {{palette.SidebarBorder}};
            }

            .settings-nav-list row {
                background: transparent;
                border-bottom: 0;
            }

            .settings-nav-row-body {
                min-height: 44px;
            }

            .settings-content-shell {
                background: {{palette.ContentBackground}};
            }

            .settings-page {
                background: transparent;
            }

            .settings-card {
                background: {{palette.SectionBackground}};
                border: 1px solid {{palette.ContentBorder}};
                padding: 16px;
            }

            .avatar-preview-frame {
                background: {{palette.SectionBackground}};
                border: 1px solid {{palette.ContentBorder}};
                padding: 0;
            }

            .settings-card-muted {
                background: {{palette.SectionMutedBackground}};
                border: 1px solid {{palette.ContentBorder}};
                padding: 16px;
            }

            .catalog-description-view,
            .catalog-description-view viewport {
                background: {{palette.SectionMutedBackground}};
                border: 1px solid {{palette.ContentBorder}};
            }

            .catalog-description-text,
            .catalog-description-heading,
            .catalog-description-code {
                color: {{palette.PrimaryText}};
            }

            .catalog-description-code-shell {
                background: {{palette.ContentBackground}};
                border: 1px solid {{palette.ContentBorder}};
            }

            notebook {
                background: transparent;
                border: 0;
            }

            notebook header {
                background: transparent;
                border-bottom: 1px solid {{palette.ContentBorder}};
            }

            notebook tab {
                background: {{palette.SectionMutedBackground}};
                color: {{palette.SecondaryText}};
                border: 1px solid {{palette.ContentBorder}};
                border-bottom: 0;
                padding: 8px 14px;
            }

            notebook tab:checked {
                background: {{palette.SectionBackground}};
                color: {{palette.PrimaryText}};
            }

            notebook stack {
                background: transparent;
            }

            .asset-tile-button {
                background: {{palette.SectionBackground}};
                color: {{palette.PrimaryText}};
                border: 1px solid {{palette.ContentBorder}};
                padding: 0;
            }

            .asset-tile-button:hover {
                background: {{palette.RowHover}};
            }

            .asset-tile-button-selected {
                background: {{palette.RowSelected}};
                border-color: {{palette.Accent}};
            }

            .asset-thumb-shell {
                background: {{palette.SectionMutedBackground}};
                border: 1px solid {{palette.ContentBorder}};
            }

            .asset-thumb-placeholder {
                background: {{palette.SectionMutedBackground}};
                border: 0;
            }

            .asset-tile-label {
                color: {{palette.PrimaryText}};
                font-size: 12px;
                font-weight: 600;
            }

            .settings-row {
                border-bottom: 1px solid {{palette.ContentBorder}};
                padding: 10px 0;
            }

            .settings-footer {
                background: {{palette.TopbarBackground}};
                border-top: 1px solid {{palette.TopbarBorder}};
            }

            .add-instance-identity-shell {
                background: {{palette.TopbarBackground}};
                border-bottom: 1px solid {{palette.TopbarBorder}};
            }

            .add-instance-nav-shell {
                min-width: 186px;
            }

            .add-instance-source-list row {
                background: transparent;
                border-bottom: 0;
            }

            .add-instance-source-list row:hover {
                background: {{palette.RowHover}};
            }

            .add-instance-source-list row:selected {
                background: {{palette.Accent}};
            }

            .add-instance-source-list row:selected .settings-nav-text,
            .add-instance-source-list row:selected label {
                color: #ffffff;
            }

            .add-instance-source-list row:selected .settings-nav-accent {
                background: #ffffff;
            }

            .add-instance-page {
                background: transparent;
            }

            .add-instance-content-shell {
                background: {{palette.ContentBackground}};
            }

            .add-instance-card {
                padding: 14px;
            }

            .add-instance-pane {
                background: transparent;
            }

            .add-instance-icon-placeholder {
                background: {{palette.SectionMutedBackground}};
                border: 1px solid {{palette.ContentBorder}};
            }

            .add-instance-icon-text {
                color: {{palette.Accent}};
                font-size: 17px;
                font-weight: 700;
            }

            .add-instance-field-label,
            .add-instance-header-label {
                color: {{palette.PrimaryText}};
                font-weight: 600;
                font-size: 13px;
            }

            .add-instance-search-label {
                margin-top: 2px;
            }

            .add-instance-list-header {
                background: {{palette.SectionMutedBackground}};
                border: 1px solid {{palette.ContentBorder}};
                padding: 0;
            }

            .add-instance-version-list row,
            .add-instance-pack-list row {
                border-bottom: 1px solid {{palette.ContentBorder}};
            }

            .add-instance-version-row-even {
                background: {{palette.RowBackground}};
            }

            .add-instance-version-row-odd {
                background: {{palette.SectionMutedBackground}};
            }

            .add-instance-version-list row:hover,
            .add-instance-pack-list row:hover {
                background: {{palette.RowHover}};
            }

            .add-instance-pack-list row {
                background: {{palette.RowBackground}};
            }

            .add-instance-pack-row-even {
                background: {{palette.RowBackground}};
            }

            .add-instance-pack-row-odd {
                background: {{palette.SectionMutedBackground}};
            }

            .add-instance-version-list row:selected,
            .add-instance-pack-list row:selected {
                background: {{palette.Accent}};
            }

            .add-instance-version-list row:selected label,
            .add-instance-pack-list row:selected label,
            .add-instance-pack-list row:selected .settings-section-title,
            .add-instance-pack-list row:selected .settings-help {
                color: #ffffff;
            }

            .add-instance-filter-check {
                margin-top: 2px;
                margin-bottom: 2px;
            }

            .add-instance-loader-preview {
                background: {{palette.SectionMutedBackground}};
                border: 1px solid {{palette.ContentBorder}};
                min-height: 250px;
            }

            .add-instance-loader-choice-shell {
                background: {{palette.SectionBackground}};
            }

            .add-instance-pane separator {
                background: {{palette.ContentBorder}};
            }

            .add-instance-loader-preview-title {
                color: {{palette.PrimaryText}};
                font-size: 18px;
                font-weight: 700;
            }

            .add-instance-version-cell {
                background: transparent;
            }

            .add-instance-version-cell-divider {
                border-right: 1px dashed {{palette.ContentBorder}};
            }

            .add-instance-column-divider {
                color: {{palette.ContentBorder}};
                font-size: 14px;
                font-weight: 600;
            }

            .add-instance-pack-row-body {
                background: transparent;
            }

            .add-instance-pack-icon-shell {
                background: {{palette.SectionBackground}};
                border: 1px solid {{palette.ContentBorder}};
            }

            .add-instance-pack-icon-placeholder {
                color: {{palette.Accent}};
                font-family: {{LauncherFontBootstrapper.PreferredTitleFontFamily}};
                font-weight: 600;
            }

            .add-instance-pack-list row:selected .add-instance-pack-icon-shell {
                border-color: rgba(255, 255, 255, 0.5);
                background: rgba(255, 255, 255, 0.08);
            }

            .add-instance-pack-list row:selected .add-instance-pack-icon-placeholder {
                color: #ffffff;
            }

            .add-instance-pack-details {
                background: transparent;
            }

            .add-instance-footer {
                background: {{palette.TopbarBackground}};
                border-top: 1px solid {{palette.TopbarBorder}};
            }

            .add-instance-footer-primary {
                min-width: 96px;
            }

            .add-instance-footer-secondary {
                min-width: 96px;
            }
            """;
    }
}
