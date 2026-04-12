using Gdk;
using Gtk;
using BlockiumLauncher.UI.GtkSharp.Services;

namespace BlockiumLauncher.UI.GtkSharp.Styling;

public sealed record LauncherThemePalette(
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
        var palette = UiPreferences.CurrentPalette;
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
            viewport,
            .launcher-window-root,
            .launcher-window-root viewport,
            .launcher-window-root box {
                background: {{palette.ContentBackground}};
                color: {{palette.PrimaryText}};
            }

            .settings-window,
            .settings-window > box {
                background: {{palette.WindowBackground}};
            }

            dialog .dialog-action-area,
            dialog actionbar {
                background: {{palette.TopbarBackground}};
                border-top: 1px solid {{palette.TopbarBorder}};
            }

            .launcher-dialog-shell,
            .launcher-dialog-shell viewport,
            .launcher-dialog-shell box,
            .launcher-section-shell,
            .launcher-section-shell viewport,
            .launcher-section-shell box {
                background: {{palette.ContentBackground}};
                color: {{palette.PrimaryText}};
            }

            .launcher-dialog-shell {
                border: 1px solid {{palette.TopbarBorder}};
            }

            .launcher-window-root {
                background: {{palette.ContentBackground}};
            }

            .launcher-body {
                background: {{palette.ContentBackground}};
            }

            .launcher-section-shell {
                border: 0;
            }

            .launcher-dialog-footer {
                padding-top: 6px;
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
                min-height: 18px;
                padding: 6px 8px;
            }

            spinbutton {
                background: {{palette.SearchBackground}};
                color: {{palette.PrimaryText}};
                border: 1px solid {{palette.SearchBorder}};
                min-height: 18px;
                padding: 0;
            }

            spinbutton entry {
                background: transparent;
                color: {{palette.PrimaryText}};
                border: 0;
                box-shadow: none;
                padding: 6px 8px;
            }

            spinbutton button {
                background: {{palette.ActionButtonBackground}};
                color: {{palette.PrimaryText}};
                border: 0;
                border-left: 1px solid {{palette.SearchBorder}};
                box-shadow: none;
                min-width: 22px;
                padding: 0;
            }

            spinbutton button:hover {
                background: {{palette.ActionButtonHover}};
            }

            spinbutton button:disabled,
            spinbutton:disabled,
            spinbutton:disabled entry {
                background: {{palette.SectionMutedBackground}};
                color: {{palette.SecondaryText}};
                border-color: {{palette.ContentBorder}};
            }

            entry placeholder {
                color: {{palette.SecondaryText}};
            }

            combobox.app-combo-field box.linked button,
            .app-combo-field box.linked button {
                background: {{palette.SearchBackground}};
                color: {{palette.PrimaryText}};
                border: 1px solid {{palette.SearchBorder}};
                min-height: 18px;
                padding: 6px 8px;
            }

            entry selection,
            textview text selection {
                background: {{palette.Accent}};
                color: #ffffff;
            }

            entry:focus,
            combobox:focus,
            combobox box.linked button:focus,
            spinbutton:focus,
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
                min-height: 16px;
                padding: 5px 8px;
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
                padding: 4px 10px;
            }

            headerbar.topbar-shell.dialog-topbar-shell {
                min-height: 0;
                padding: 4px 10px;
            }

            headerbar.topbar-shell:backdrop {
                background: {{palette.TopbarBackground}};
                background-image: none;
            }

            headerbar.topbar-shell box,
            headerbar.topbar-shell box.horizontal,
            headerbar.topbar-shell box.vertical,
            headerbar.topbar-shell widget,
            headerbar.topbar-shell centerbox,
            headerbar.topbar-shell decoration {
                background: transparent;
                background-image: none;
                box-shadow: none;
                border: 0;
            }

            .topbar-content,
            .toolbar-group,
            .browser-controls,
            .sidebar-actions,
            .popover-content,
            .launcher-popover-content {
                background: transparent;
            }

            popover.background > box,
            popover.background box,
            popover.background scrolledwindow,
            popover.background viewport,
            popover.background frame {
                background: {{palette.PopoverBackground}};
                background-image: none;
            }

            popover.background scrolledwindow,
            popover.background viewport,
            popover.background frame {
                border: 0;
                box-shadow: none;
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
                padding-bottom: 12px;
            }

            .instance-name,
            .settings-title,
            .settings-page-title,
            .popover-title {
                color: {{palette.PrimaryText}};
                font-family: {{LauncherFontBootstrapper.PreferredTitleFontFamily}};
                font-weight: 600;
            }

            .settings-page-title-link,
            .settings-page-title-link:hover,
            .settings-page-title-link:active,
            .settings-page-title-link:focus,
            .settings-page-title-link:disabled {
                padding: 0;
                margin: 0;
                min-height: 0;
                min-width: 0;
                border: 0;
                border-radius: 0;
                background: transparent;
                background-image: none;
                box-shadow: none;
            }

            .settings-page-title-link .settings-page-title {
                color: {{palette.Accent}};
            }

            .settings-page-title-link:disabled .settings-page-title {
                color: {{palette.PrimaryText}};
            }

            .instance-name,
            .settings-title {
                font-size: 15px;
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
                font-size: 12px;
            }

            headerbar.topbar-shell.dialog-topbar-shell .settings-title {
                font-size: 14px;
            }

            headerbar.topbar-shell.dialog-topbar-shell .settings-subtitle {
                font-size: 11px;
            }

            .settings-section-title {
                color: {{palette.PrimaryText}};
                font-family: {{LauncherFontBootstrapper.PreferredTitleFontFamily}};
                font-weight: 600;
                font-size: 13px;
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
                padding: 5px 10px;
            }

            .toolbar-button:hover,
            .square-icon-button:hover,
            .theme-toggle-button:hover {
                background: {{palette.ToolbarButtonHover}};
            }

            .toolbar-button:disabled,
            .toolbar-button:disabled label,
            .square-icon-button:disabled,
            .square-icon-button:disabled label,
            .square-icon-button:disabled image,
            .theme-toggle-button:disabled,
            .theme-toggle-button:disabled label,
            .theme-toggle-button:disabled image {
                background: {{palette.SectionMutedBackground}};
                color: {{palette.SecondaryText}};
                border-color: {{palette.ContentBorder}};
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
                padding: 6px 10px;
            }

            .action-button-label {
                color: inherit;
            }

            .action-button:hover,
            .popover-menu-button:hover {
                background: {{palette.ActionButtonHover}};
            }

            .action-button:disabled,
            .action-button:disabled label,
            .popover-menu-button:disabled,
            .popover-menu-button:disabled label,
            .danger-button:disabled,
            .danger-button:disabled label {
                background: {{palette.SectionMutedBackground}};
                color: {{palette.SecondaryText}};
                border-color: {{palette.ContentBorder}};
            }

            .primary-button {
                background: {{palette.PrimaryButton}};
                color: #ffffff;
                border-color: {{palette.PrimaryButton}};
                transition: all 0.15s ease-in-out;
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
            .primary-button:checked image {
                color: #ffffff;
            }

            .primary-button:hover,
            .primary-button:checked {
                background: {{palette.PrimaryButtonHover}};
            }

            .primary-button:active {
                background: shade({{palette.PrimaryButtonHover}}, 0.9);
                box-shadow: inset 0 3px 5px rgba(0, 0, 0, 0.2);
            }

            .primary-button:disabled {
                background: {{palette.SectionMutedBackground}};
                border-color: {{palette.ContentBorder}};
                color: {{palette.SecondaryText}};
            }

            .primary-button:disabled label,
            .primary-button:disabled image {
                color: {{palette.SecondaryText}};
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
                background: {{palette.ContentBackground}};
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
                background: {{palette.ContentBackground}};
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
                min-height: 38px;
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
                padding: 10px;
            }

            .avatar-preview-frame {
                background: {{palette.SectionBackground}};
                border: 1px solid {{palette.ContentBorder}};
                padding: 0;
            }

            .settings-card-muted {
                background: {{palette.SectionMutedBackground}};
                border: 1px solid {{palette.ContentBorder}};
                padding: 10px;
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

            .manual-download-list-scroller,
            .manual-download-list-scroller viewport {
                background: {{palette.SectionMutedBackground}};
                border: 1px solid {{palette.ContentBorder}};
            }

            .manual-download-row-shell {
                background: {{palette.SectionBackground}};
                border: 1px dashed {{palette.ContentBorder}};
            }

            .manual-download-row-shell-resolved {
                background: rgba(76, 201, 114, 0.16);
                border-color: rgba(76, 201, 114, 0.55);
            }

            .manual-download-status,
            .manual-download-row-status {
                color: {{palette.SecondaryText}};
            }

            .manual-download-row-shell-resolved .manual-download-row-status,
            .manual-download-link-button-resolved {
                color: #2f9e44;
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
                padding: 7px 0;
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
                padding: 10px;
            }

            .add-instance-pane {
                background: transparent;
            }

            .add-instance-list-scroller,
            .add-instance-list-scroller viewport {
                background: {{palette.SectionMutedBackground}};
                border: 1px solid {{palette.ContentBorder}};
            }

            .add-instance-version-list,
            .add-instance-pack-list,
            .add-instance-version-list viewport,
            .add-instance-pack-list viewport,
            .add-instance-version-list box,
            .add-instance-pack-list box {
                background: {{palette.SectionMutedBackground}};
                color: {{palette.PrimaryText}};
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
                border-bottom: 0;
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

            .add-instance-version-list row:selected box,
            .add-instance-version-list row:selected eventbox,
            .add-instance-version-list row:selected .add-instance-version-cell,
            .add-instance-version-list row:selected .add-instance-item-shell,
            .add-instance-version-list row:selected .add-instance-version-row-even,
            .add-instance-version-list row:selected .add-instance-version-row-odd,
            .add-instance-pack-list row:selected box,
            .add-instance-pack-list row:selected eventbox,
            .add-instance-pack-list row:selected .add-instance-item-shell,
            .add-instance-pack-list row:selected .add-instance-pack-row-body {
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

            .add-instance-item-shell {
                background: transparent;
                border: 1px dashed {{palette.ContentBorder}};
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

            .add-instance-pack-icon-cell {
                border-right: 1px dashed {{palette.ContentBorder}};
            }

            .add-instance-pack-title {
                color: {{palette.PrimaryText}};
                font-family: {{LauncherFontBootstrapper.PreferredTitleFontFamily}};
                font-weight: 600;
                font-size: 13px;
            }

            .add-instance-pack-icon-placeholder {
                color: {{palette.Accent}};
                font-family: {{LauncherFontBootstrapper.PreferredTitleFontFamily}};
                font-weight: 600;
            }

            .add-instance-pack-list row:selected .add-instance-pack-icon-cell {
                border-right-color: rgba(255, 255, 255, 0.55);
            }

            .add-instance-pack-list row:selected .add-instance-pack-icon-placeholder {
                color: #ffffff;
            }

            .launcher-crash-scroller,
            .launcher-crash-scroller viewport,
            .launcher-crash-text,
            .launcher-crash-text text {
                background: {{palette.SectionMutedBackground}};
                color: {{palette.PrimaryText}};
                border: 1px solid {{palette.ContentBorder}};
            }

            .edit-instance-nav-shell {
                border-right: 1px solid {{palette.SidebarBorder}};
            }

            .edit-instance-log-shell,
            .edit-instance-log-shell viewport,
            .edit-instance-log-view,
            .edit-instance-log-view text {
                background: {{palette.SectionMutedBackground}};
                color: {{palette.PrimaryText}};
                border: 1px solid {{palette.ContentBorder}};
            }

            .edit-instance-content-list {
                background: transparent;
            }

            .edit-instance-sort-header,
            .edit-instance-sort-header:hover,
            .edit-instance-sort-header:active,
            .edit-instance-sort-header:focus {
                padding: 0;
                min-height: 0;
                min-width: 0;
                border: 0;
                border-radius: 0;
                background: transparent;
                background-image: none;
                box-shadow: none;
                color: {{palette.SecondaryText}};
            }

            .edit-instance-sort-header label {
                color: {{palette.SecondaryText}};
                font-weight: 600;
            }

            .edit-instance-actions-cell {
                background: transparent;
            }

            .edit-instance-actions-sidebar {
                background: {{palette.SectionBackground}};
                min-width: 210px;
            }

            .edit-instance-actions-sidebar button {
                margin-top: 2px;
                margin-bottom: 2px;
            }

            .edit-instance-content-row {
                background: {{palette.SectionBackground}};
                border-bottom: 1px solid {{palette.ContentBorder}};
            }

            .edit-instance-content-row:hover {
                background: {{palette.RowHover}};
            }

            .edit-instance-content-list row:selected button,
            .edit-instance-content-list row:selected button label,
            .edit-instance-content-list row:selected .edit-instance-source-badge,
            .edit-instance-content-list row:selected .edit-instance-source-badge label {
                color: #ffffff;
                border-color: rgba(255, 255, 255, 0.45);
            }

            .edit-instance-content-list row:selected .action-button,
            .edit-instance-content-list row:selected .danger-button,
            .edit-instance-content-list row:selected .edit-instance-source-badge {
                background: rgba(255, 255, 255, 0.14);
            }

            .edit-instance-content-list row:selected .settings-caption,
            .edit-instance-content-list row:selected .catalog-installed-label {
                color: rgba(255, 255, 255, 0.88);
            }

            .provider-inline-link,
            .provider-inline-link:hover,
            .provider-inline-link:active,
            .provider-inline-link:focus {
                padding: 0;
                min-height: 0;
                min-width: 0;
                border: 0;
                border-radius: 0;
                background: transparent;
                background-image: none;
                box-shadow: none;
            }

            .provider-inline-link,
            .provider-inline-link label {
                color: {{palette.Accent}};
            }

            .catalog-browser-nav-shell {
                min-width: 118px;
            }

            .catalog-browser-content-shell {
                background: {{palette.ContentBackground}};
            }

            .catalog-browser-nav-shell .settings-nav-row-body {
                min-height: 30px;
            }

            .catalog-installed-label {
                color: {{palette.SecondaryText}};
            }

            .edit-instance-source-badge {
                background: {{palette.ToolbarButtonBackground}};
                color: {{palette.PrimaryText}};
                border: 1px solid {{palette.ContentBorder}};
                padding: 2px 6px;
            }

            .add-instance-pack-details {
                background: {{palette.SectionBackground}};
            }

            .add-instance-loader-status {
                color: {{palette.SecondaryText}};
            }

            .add-instance-footer {
                background: {{palette.TopbarBackground}};
                border-top: 1px solid {{palette.TopbarBorder}};
            }

            .add-instance-footer-primary {
                min-width: 88px;
            }

            .add-instance-footer-secondary {
                min-width: 88px;
            }
            """;
    }
}
