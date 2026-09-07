-- Full LuaRT UI example.
-- Rename this file to theme.lua to run it with the bundled LuaRT runtime.

local ui = require "ui"
require "canvas"

ui.theme = "dark"

local win = ui.Window("LuaRT Cursor Gradient", "fixed", 800, 500)
local canvas = ui.Canvas(win)
canvas.align = "all"
canvas.cursor = "cross"
canvas.bgcolor = 0x10131CFF

local cursorX = canvas.width * 0.5
local cursorY = canvas.height * 0.5
local phase = 0

local gradient = canvas:RadialGradient {
    [0.00] = 0xFF4D8DFF,
    [0.35] = 0x7C5CFFFF,
    [0.72] = 0x163B80AA,
    [1.00] = 0x00000000
}
gradient.center = { cursorX, cursorY }
gradient.radius = { 260, 260 }

canvas.font = "Segoe UI"
canvas.fontsize = 20
canvas.color = 0xFFFFFFFF

function canvas:onHover(x, y)
    cursorX = x
    cursorY = y
    gradient.center = { x, y }
end

function win:onResize()
    gradient.radius = { self.width * 0.36, self.height * 0.55 }
end

function canvas:onPaint()
    self:begin()
    self:clear()

    -- Paint the full background with a gradient centered on the cursor.
    self:fillrect(0, 0, self.width, self.height, gradient)

    -- Draw a pulsing ring around the cursor.
    local pulse = 28 + math.sin(phase) * 6
    self:circle(cursorX, cursorY, pulse, 0xFFFFFFFF, 2)
    self:fillcircle(cursorX, cursorY, 5, 0xFFFFFFFF)

    self:print("LuaRT Canvas", 24, 24)
    self:print("Move the cursor across the canvas", 24, 52)

    self:flip()
end

-- LuaRT Tasks are cooperative. This task updates the animation state without
-- blocking the UI task or the canvas paint callback.
local animation = sys.Task(function()
    while true do
        phase = phase + 0.08
        sleep(16)
    end
end)
animation()

win:center()
win:showasync():wait()
