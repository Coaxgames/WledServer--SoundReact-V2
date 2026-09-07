# LuaRT themes

The active default theme is `Themes/CanvasCursor/theme.lua`. Replace that file or add another `theme.lua` under `Themes/` to select a different theme.

The server starts the first `theme.lua` it finds when the application starts. The script runs with the bundled LuaRT runtime and can use the shipped `LuaRT/modules` and `LuaRT/lualibs` directories through `require`.

This currently provides the full LuaRT runtime process. WLED-specific data and drawing commands will be added through the host integration API.

## Canvas example

The active cursor-gradient theme creates a real LuaRT window and Canvas, follows the mouse with a radial gradient, draws a pulse animation, and uses a LuaRT Task for its animation state.

LuaRT colors use `0xRRGGBBAA` values. Canvas drawing must be wrapped by `begin()` and `flip()`:

```lua
function canvas:onPaint()
    self:begin()
    self:clear()
    self:fillrect(0, 0, self.width, self.height, gradient)
    self:flip()
end
```

`canvas:onHover(x, y)` receives the cursor coordinates. A `RadialGradient` can then be moved by assigning its `center` property:

```lua
function canvas:onHover(x, y)
    gradient.center = { x, y }
end
```

Keep `theme.lua` running for as long as the theme should remain active. LuaRT tasks can be used for background work:

```lua
local task = sys.Task(function()
    while true do
        -- Theme work goes here.
        sleep(16)
    end
end)

task()
```
