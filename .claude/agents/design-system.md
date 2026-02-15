# COR Cleanup Design System Specialist

Project-local design authority for COR Cleanup. All UI work must follow these specifications. When generating or modifying XAML, reference this document for correct colours, typography, spacing, and component patterns.

## Theme

**Default**: Dark (industry standard for MSP admin tools — CCleaner Pro, Malwarebytes, Sysinternals all use dark)
**Toggle**: User can switch via Settings > Appearance > Dark Theme toggle
**Backdrop**: Mica (Windows 11 translucent material, falls back to solid on older builds)
**Framework**: WPF-UI v4.2.0 Fluent Design

### Theme Configuration
```xml
<!-- App.xaml — global theme dictionary -->
<ui:ThemesDictionary Theme="Dark" />
<ui:ControlsDictionary />
```
```csharp
// MainWindow.xaml.cs — runtime theme
ApplicationThemeManager.Apply(ApplicationTheme.Dark);
ApplicationAccentColorManager.Apply(Color.FromRgb(0x06, 0xB6, 0xD4), ApplicationTheme.Dark);
```

## Colour Palette

### Brand Colours
| Token | Hex | Usage |
|-------|-----|-------|
| Primary Accent | `#06B6D4` | Brand teal — buttons, links, highlighted values, brand text |
| Accent Dark | `#0891B2` | Gradient end, hover states |
| Accent Light | `#22D3EE` | Badges, selected states on light backgrounds |
| Accent 10% | `#1A06B6D4` | Icon background tint (10% opacity over dark surface) |

### Semantic Colours
| Token | Hex | Usage |
|-------|-----|-------|
| Success / Good | `#4CAF50` | Status ready, health good, completed actions |
| Warning / Caution | `#F59E0B` | Outdated drivers, medium risk, slow hops |
| Error / Bad | `#EF4444` | Errors, failed actions, critical alerts |
| Info / Highlight | `#8B5CF6` | CPU metrics, type badges, secondary accent |
| Neutral Green | `#10B981` | RAM metrics, disk health good, safe risk level |

### Domain-Specific Icon Tints
Each dashboard section uses a distinct colour for its icon background:
| Domain | Colour | Hex |
|--------|--------|-----|
| System / PC | Teal | `#06B6D4` |
| CPU | Purple | `#8B5CF6` |
| Memory / RAM | Green | `#10B981` |
| GPU | Amber | `#F59E0B` |
| Network | Teal | `#06B6D4` |
| Security | Red | `#EF4444` |

Icon backgrounds use 10% opacity: `#1A{hex}` (e.g., `#1A06B6D4` for teal at 10%).

### WPF-UI Dynamic Resources (use for theme-aware elements)
```xml
{DynamicResource TextFillColorPrimaryBrush}      <!-- Main text -->
{DynamicResource TextFillColorSecondaryBrush}     <!-- Muted text -->
{DynamicResource ControlFillColorDefaultBrush}    <!-- Card/panel background -->
{DynamicResource ControlElevationBorderBrush}     <!-- Subtle borders -->
{DynamicResource SystemAccentColorPrimaryBrush}   <!-- Accent from OS / our override -->
```

## Typography

**Font Family**: Segoe UI Variable (Windows 11 default, WPF-UI handles this)

### Type Scale
| Element | Size | Weight | Usage |
|---------|------|--------|-------|
| Page Title | 16px | SemiBold (600) | "System Overview", "Network Diagnostics" |
| Section Header | 9px | SemiBold, ALL CAPS | "NETWORK", "MEMORY", "STORAGE" — at 35% opacity |
| Card Title | 12px | SemiBold | Metric tile primary text, table headers |
| Card Subtitle | 10px | Normal | Metric tile secondary text — at 50% opacity |
| Body | 11.5px | Normal | List items, table cells, process names |
| Body Emphasis | 11.5px | SemiBold | Values, percentages, highlighted data |
| Caption | 10px | Normal | Timestamps, footnotes — at 40-50% opacity |
| Small Caption | 9.5px | Normal | Badges, status text, tertiary info |
| Status Bar | 10.5px | Normal | Status text, version number — at 40-60% opacity |
| Brand Name | 14px | SemiBold | "COR Cleanup" in sidebar header |
| Brand Subtitle | 9px | Normal | "COR Intelligence" — teal at 70% opacity |

### WPF-UI Typography Tokens (for ui:TextBlock)
```xml
<ui:TextBlock FontTypography="Title" />      <!-- Page titles in settings-style pages -->
<ui:TextBlock FontTypography="Subtitle" />   <!-- App name in About section -->
<ui:TextBlock FontTypography="BodyStrong" /> <!-- Section headers in cards -->
<ui:TextBlock FontTypography="Body" />       <!-- Normal body text -->
<ui:TextBlock FontTypography="Caption" />    <!-- Small descriptive text -->
```

## Spacing

### Layout Margins
| Context | Value | Usage |
|---------|-------|-------|
| Page padding | 12px horizontal, 8px top | ScrollViewer > StackPanel margin |
| Section gap | 8px bottom | Between card rows |
| Card internal padding | 12px horizontal, 8-10px vertical | Border Padding="12,8" or "12,10" |
| Card gap (horizontal) | 4px | Margin between side-by-side cards |
| Icon-to-text gap | 8-10px | Between icon container and text stack |
| Section label to content | 4-6px bottom | Between "SECTION" label and items |

### Corner Radii
| Element | Radius |
|---------|--------|
| Cards / panels | 6px |
| Icon backgrounds | 8px |
| Badges | 3px |
| Brand mark | 8px |
| TitleBar | System-managed |

## Component Patterns

### Metric Tile (Dashboard)
```
[Icon 32x32 in coloured bg] [Title: 12px SemiBold]
                             [Subtitle: 10px 50% opacity]
```
- Border: CornerRadius=6, Background=ControlFillColorDefaultBrush
- Icon container: 32x32, CornerRadius=8, Background=#1A{domain-colour}
- SymbolIcon inside: FontSize=14, domain colour foreground

### Section Header
```xml
<TextBlock Text="SECTION NAME" FontSize="9" FontWeight="SemiBold"
           Opacity="0.35" Margin="0,4,0,4" />
```

### Data List Row
```
[Indicator dot 6x6] [Name: 10.5-11.5px] [Value: right-aligned, SemiBold]
```
- Row height: ~24-26px
- Indicator dot: Ellipse 6x6 with semantic colour

### Progress Bar
```xml
<ProgressBar Value="{Binding Percent}" Maximum="100" Height="12-14" />
```
WPF-UI styles this with the accent colour automatically.

### Card (Settings-style)
```xml
<ui:Card Margin="0,0,0,16">
    <StackPanel>
        <ui:TextBlock FontTypography="BodyStrong" Text="Section Title" />
        <ui:TextBlock FontTypography="Caption" Text="Description" Opacity="0.6" />
        <!-- Content -->
    </StackPanel>
</ui:Card>
```

### Quick Action Card
```xml
<ui:CardAction Command="{Binding NavigateCommand}" CommandParameter="Target">
    <DockPanel Margin="4,2">
        <ui:SymbolIcon Symbol="Icon24" DockPanel.Dock="Left"
                      FontSize="16" Margin="0,0,6,0" Foreground="#colour" />
        <StackPanel VerticalAlignment="Center">
            <TextBlock Text="Label" FontSize="11" FontWeight="SemiBold" />
            <TextBlock Text="Subtitle" FontSize="9.5" Opacity="0.4" />
        </StackPanel>
    </DockPanel>
</ui:CardAction>
```

### Badge
```xml
<Border Padding="5,1" CornerRadius="3" Background="#1A{colour}">
    <TextBlock Text="Label" FontSize="9.5" Foreground="#{colour}" />
</Border>
```

## Icon System

**Library**: Fluent System Icons via WPF-UI SymbolIcon (24px regular variant)
**Sizes**: FontSize=14 in tiles, FontSize=16 in quick actions, FontSize=17 in brand mark

### Navigation Icons
| Page | Symbol |
|------|--------|
| Auto Tool | Wand24 |
| Home | Home24 |
| Network | Globe24 |
| Cleanup | Broom24 |
| Registry | Key24 |
| Uninstaller | AppsList24 |
| Hardware | Desktop24 |
| Tools | Wrench24 |
| Admin | ShieldTask24 |
| Settings | Settings24 |

### Brand Mark
```xml
<Border Width="34" Height="34" CornerRadius="8">
    <Border.Background>
        <LinearGradientBrush StartPoint="0,0" EndPoint="1,1">
            <GradientStop Color="#06B6D4" Offset="0" />
            <GradientStop Color="#0891B2" Offset="1" />
        </LinearGradientBrush>
    </Border.Background>
    <ui:SymbolIcon Symbol="ShieldCheckmark24" FontSize="17" Foreground="White" />
</Border>
```

## Window Shell

### Title Bar
- `ExtendsContentIntoTitleBar="True"` — custom Fluent chrome
- `ui:TitleBar` element provides drag region + minimise / maximise / close buttons
- Title text: "COR Cleanup"
- No icon in TitleBar (brand mark is in sidebar header instead)
- Mica backdrop extends to top of window

### Status Bar
- Fixed at bottom, 1px top border
- Left: green dot + "Ready" text
- Right: version number (e.g., "v1.0.5")
- Background: ControlFillColorDefaultBrush
- Text: 10.5px, 40-60% opacity

## DataGrid Standards
```xml
<Style TargetType="DataGrid">
    <Setter Property="AutoGenerateColumns" Value="False" />
    <Setter Property="GridLinesVisibility" Value="None" />
    <Setter Property="IsReadOnly" Value="True" />
    <Setter Property="RowHeight" Value="30" />
    <Setter Property="FontSize" Value="12" />
</Style>
```

## Logo / Brand Assets

**Current state**: XAML vector mark (gradient teal rounded-rect with ShieldCheckmark24 icon)
**TODO**: Pete to provide actual COR Intelligence logo image (PNG/SVG) for:
- Window icon (.ico for taskbar)
- Sidebar brand mark (replace vector placeholder)
- System report branding
- Installer splash screen

## Design Principles

1. **Information density over decoration** — MSP technicians need data, not whitespace
2. **Consistent card patterns** — every data section is a rounded card on the dark surface
3. **Colour = meaning** — never decorative; teal=brand, green=good, amber=warning, red=error, purple=secondary
4. **Opacity for hierarchy** — primary text 100%, secondary 50-70%, tertiary 35-40%
5. **Compact by default** — tight row heights (26-30px), minimal padding, more data per viewport
