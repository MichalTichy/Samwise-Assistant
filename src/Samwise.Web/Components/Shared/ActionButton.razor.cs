using LumexUI.Common;
using Microsoft.AspNetCore.Components;

namespace Samwise.Web.Components.Shared;

public partial class ActionButton
{
    private bool running;

    [Parameter] public ButtonType Type { get; set; } = ButtonType.Button;
    [Parameter] public Variant Variant { get; set; } = Variant.Solid;
    [Parameter] public ThemeColor Color { get; set; } = ThemeColor.Default;
    [Parameter] public Size Size { get; set; } = Size.Medium;
    [Parameter] public bool FullWidth { get; set; }
    [Parameter] public bool Disabled { get; set; }
    [Parameter] public bool Pending { get; set; }
    [Parameter] public string? Class { get; set; }
    [Parameter] public EventCallback OnClick { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }

    private bool Busy => Pending || running;

    private async Task HandleClickAsync()
    {
        if (Disabled || Busy || !OnClick.HasDelegate)
        {
            return;
        }

        running = true;
        try
        {
            await OnClick.InvokeAsync();
        }
        finally
        {
            running = false;
        }
    }
}
