using System.Drawing;
using Nook;

namespace Nook.Tests;

/// <summary>
/// A locked overlay is click-through, so once it sits where no screen is it cannot be found or
/// dragged back. These tests pin where it is put at startup and after the display layout changes.
/// </summary>
public class OverlayPlacementTests
{
    private static readonly Rectangle Primary = new(0, 0, 1920, 1040);
    private static readonly Rectangle RightMonitor = new(1920, 0, 2560, 1400);
    private static readonly Size Overlay = new(240, 80);

    [Fact]
    public void A_fully_visible_overlay_stays_where_it_is()
    {
        var placed = OverlayForm.PlaceOnScreen(new Rectangle(new Point(2400, 300), Overlay), [Primary, RightMonitor], Primary);

        Assert.Equal(new Point(2400, 300), placed);
    }

    [Fact]
    public void An_overlay_on_an_unplugged_monitor_moves_to_the_primary_screen()
    {
        var placed = OverlayForm.PlaceOnScreen(new Rectangle(new Point(4000, 1200), Overlay), [Primary], Primary);

        Assert.Equal(new Point(1920 - 240, 1040 - 80), placed);
    }

    [Fact]
    public void An_overlay_hanging_off_an_edge_is_pulled_back_onto_its_own_screen()
    {
        var placed = OverlayForm.PlaceOnScreen(new Rectangle(new Point(4400, -30), Overlay), [Primary, RightMonitor], Primary);

        Assert.Equal(new Point(1920 + 2560 - 240, 0), placed);
    }

    [Fact]
    public void A_screen_to_the_left_of_the_primary_is_respected()
    {
        var left = new Rectangle(-1280, 0, 1280, 984);

        var placed = OverlayForm.PlaceOnScreen(new Rectangle(new Point(-1300, 100), Overlay), [Primary, left], Primary);

        Assert.Equal(new Point(-1280, 100), placed);
    }

    [Fact]
    public void An_overlay_larger_than_the_screen_is_pinned_to_its_top_left()
    {
        var tiny = new Rectangle(0, 0, 200, 60);

        var placed = OverlayForm.PlaceOnScreen(new Rectangle(new Point(50, 20), Overlay), [tiny], tiny);

        Assert.Equal(new Point(0, 0), placed);
    }

    [Fact]
    public void Without_any_screen_there_is_no_placement()
    {
        Assert.Null(OverlayForm.PlaceOnScreen(new Rectangle(new Point(10, 10), Overlay), [], null));
    }
}
