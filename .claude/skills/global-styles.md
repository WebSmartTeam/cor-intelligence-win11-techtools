# COR Cleanup — Global Design System

## Overview
COR Cleanup uses a CCleaner-inspired light theme with the COR Intelligence teal accent. All design tokens are centralised in `CORCleanup/Themes/CORStyles.xaml`.

## Colour Palette

### Backgrounds
| Token | Hex | Resource Key | Usage |
|-------|-----|-------------|-------|
| Sidebar | #EBEBEF | `CorSidebarBrush` | NavigationView pane background |
| Content | #F5F5F8 | `CorContentBrush` | Page content area (overrides ApplicationBackgroundBrush) |
| Card | #FFFFFF | `CorCardBrush` | Card surfaces, elevated content |
| Title Bar | #E8E8EC | `CorTitleBarBrush` | Window title bar |

### Brand Accent
| Token | Hex | Resource Key | Usage |
|-------|-----|-------------|-------|
| Primary | #06B6D4 | `CorAccentBrush` | Primary actions, links, active states |
| Light | #22D3EE | `CorAccentLightBrush` | Hover states |
| Dark | #0891B2 | `CorAccentDarkBrush` | Pressed states |

### Text
| Token | Hex | Resource Key | Usage |
|-------|-----|-------------|-------|
| Primary | #1A1A2E | `CorTextPrimaryBrush` | Main body text |
| Secondary | #6B7280 | `CorTextSecondaryBrush` | Labels, captions |
| Muted | #9CA3AF | `CorTextMutedBrush` | Hints, placeholders |

### Semantic
| Token | Hex | Resource Key | Usage |
|-------|-----|-------------|-------|
| Success | #10B981 | `CorSuccessBrush` | Good health, passed, completed |
| Warning | #F59E0B | `CorWarningBrush` | Caution, review needed |
| Danger | #EF4444 | `CorDangerBrush` | Bad health, errors, critical |
| Info | #3B82F6 | `CorInfoBrush` | Informational badges |

### Borders
| Token | Hex | Resource Key | Usage |
|-------|-----|-------------|-------|
| Default | #E5E7EB | `CorBorderBrush` | Soft borders, separators |
| Strong | #D1D5DB | `CorBorderStrongBrush` | Prominent borders |

## Layout Tokens

### Corner Radius
| Token | Value | Resource Key | Usage |
|-------|-------|-------------|-------|
| Button | 8 | `CorButtonRadius` | All ui:Button controls |
| Card | 8 | `CorCardRadius` | All ui:Card controls |
| Badge | 4 | `CorBadgeRadius` | Status badges, tags |
| Input | 6 | `CorInputRadius` | Text inputs, dropdowns |

### Spacing Convention
- **Card padding**: 16px (`Padding="16"`)
- **Card margin**: 0,0,0,12 (12px gap between stacked cards)
- **Section header margin**: 0,0,0,10 (10px below section title)
- **Page margin**: 12,6 (12px horizontal, 6px vertical)
- **Button padding**: Default WPF-UI (or `Padding="4,3"` for icon-only)

## Typography

### Font Family
- **Primary**: Segoe UI Variable Display (falls back to Segoe UI on older Windows)
- **Monospace**: Cascadia Code (falls back to Consolas)

### Font Sizes
| Usage | Size | Weight |
|-------|------|--------|
| Page title | 16 | SemiBold |
| Section heading | 15 | SemiBold |
| Card title (BodyStrong) | WPF-UI default | SemiBold |
| Body text | 12-13 | Normal |
| Caption/label | 11-11.5 | Normal, Opacity 0.6 |
| Status bar | 10.5 | Normal, Opacity 0.4-0.6 |
| Badge text | 10 | Normal |

## Component Patterns

### Card Pattern (standard section card)
```xaml
<ui:Card Padding="16" Margin="0,0,0,12">
    <StackPanel>
        <StackPanel Orientation="Horizontal" Margin="0,0,0,10">
            <ui:SymbolIcon Symbol="..."
                          FontSize="16" Margin="0,0,8,0"
                          Foreground="{DynamicResource SystemAccentColorPrimaryBrush}" />
            <TextBlock Text="Section Name"
                       FontSize="15" FontWeight="SemiBold" />
        </StackPanel>
        <!-- content here -->
    </StackPanel>
</ui:Card>
```

### Dashboard Tile Pattern (raw Border for lightweight cards)
```xaml
<Border Padding="10,6" CornerRadius="6"
        Background="{DynamicResource ControlFillColorDefaultBrush}">
    <DockPanel>
        <!-- icon badge + label/value -->
    </DockPanel>
</Border>
```

### Icon Badge Pattern (coloured background circle with icon)
```xaml
<Border Width="30" Height="30" CornerRadius="7"
        Background="#1A06B6D4">
    <ui:SymbolIcon Symbol="..." FontSize="13"
                  HorizontalAlignment="Center"
                  VerticalAlignment="Center" />
</Border>
```
The `#1A` prefix gives 10% opacity tint of the accent colour.

### Button Appearances
Use WPF-UI semantic appearances for context:
- `Appearance="Primary"` — main actions (teal accent)
- `Appearance="Secondary"` — default/neutral
- `Appearance="Success"` — positive actions (green)
- `Appearance="Danger"` — destructive actions (red)
- `Appearance="Caution"` — warning actions (amber)
- `Appearance="Info"` — informational actions (blue)

### Status Badge Pattern
```xaml
<Border Background="#CorSuccessBrush" CornerRadius="4"
        Padding="6,2">
    <TextBlock Text="Good" FontSize="10" Foreground="White" />
</Border>
```

## Window Structure

```
FluentWindow (rounded corners, ExtendsContentIntoTitleBar)
  Grid (3 rows)
    Row 0: TitleBar (grey, integrated with window chrome)
    Row 1: NavigationView (grey sidebar left, content right)
      MenuItems: 8 main sections
      PaneFooter: COR Intelligence logo
      FooterMenuItems: Help, Idea Portal, Settings
    Row 2: Status bar (ready indicator + version)
```

## Files
- **Design tokens**: `CORCleanup/Themes/CORStyles.xaml`
- **Window shell**: `CORCleanup/MainWindow.xaml`
- **Theme setup**: `CORCleanup/App.xaml` (merge order: ThemesDictionary → ControlsDictionary → CORStyles)
