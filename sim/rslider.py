from __future__ import annotations
import tkinter as tk

from typing import Callable


class RangeSlider(tk.Canvas):
    """A range slider with a cursor that can be resized and move"""

    def __init__(
        self,
        parent,
        slider_min=0.0,
        slider_max=100.0,
        cursor_start=20.0,
        cursor_end=80.0,
        callback: Callable[[float, float], None] | None = None,
        x_padding=10.0,
        y_padding=10.0,
        **kwargs,
    ) -> None:
        super().__init__(parent, **kwargs)

        self.slider_min = slider_min
        self.slider_max = slider_max
        self.cursor_min_width = (slider_max - slider_min) * 0.1
        self.cursor_start = cursor_start
        self.cursor_end = cursor_end
        self.callback = callback
        self.x_padding = x_padding
        self.y_padding = y_padding

        self.slider_width = 10
        self.slider_height = 20
        self.line_width = 8

        self.dragging = ""
        self.prev_mouse_x = None

        # Initialize variables
        self.start_x = 0.0
        self.end_x = 0.0

        # Draw components
        self.full_line: int
        self.range_line: int
        self.start_slider: int
        self.end_slider: int

        # bindings
        self.bind("<Configure>", self.on_resize)
        self.bind("<Button-1>", self.on_click)
        self.bind("<B1-Motion>", self.on_drag)
        self.bind("<ButtonRelease-1>", self.on_release)

    def change_size(self, slider_min=0, slider_max=100, cursor_start=20, cursor_end=80):
        self.slider_min = slider_min
        self.slider_max = slider_max
        self.cursor_min_width = (slider_max - slider_min) * 0.1
        self.cursor_start = cursor_start
        self.cursor_end = cursor_end
        self.draw_slider()
        if self.callback:
            self.callback(self.cursor_start, self.cursor_end)

    def move_cursor_right(self, move_val: float) -> None:
        move_max = self.slider_max - self.cursor_end
        move = move_max if move_val > move_max else move_val
        self.cursor_start += move
        self.cursor_end += move
        self.update_positions()
        if self.callback:
            self.callback(self.cursor_start, self.cursor_end)

    def move_cursor_left(self, move_val: float) -> None:
        move_max = self.cursor_start - self.slider_min
        move = move_max if move_val > move_max else move_val
        self.cursor_start -= move
        self.cursor_end -= move
        self.update_positions()
        if self.callback:
            self.callback(self.cursor_start, self.cursor_end)

    def shrink_cursor(self) -> None:
        value = 0.1 * (self.slider_max - self.slider_min)
        cursor_width = self.cursor_end - self.cursor_start
        if cursor_width <= self.cursor_min_width:
            return
        max_shrink = cursor_width - 1
        shrink = max_shrink if value > max_shrink else value
        self.cursor_start += shrink / 2
        self.cursor_end -= shrink / 2
        self.update_positions()
        if self.callback:
            self.callback(self.cursor_start, self.cursor_end)

    def expand_cursor(self) -> None:
        value = 0.1 * (self.slider_max - self.slider_min)
        cursor_width = self.cursor_end - self.cursor_start
        if cursor_width >= self.slider_max - self.slider_min:
            return
        max_expand = self.slider_max - self.slider_min - cursor_width
        expand = max_expand if value > max_expand else value
        self.cursor_start -= expand / 2
        self.cursor_end += expand / 2
        if self.cursor_start < self.slider_min:
            self.move_cursor_right(self.slider_min - self.cursor_start)
        if self.cursor_end > self.slider_max:
            self.move_cursor_left(self.cursor_end - self.slider_max)
        self.update_positions()
        if self.callback:
            self.callback(self.cursor_start, self.cursor_end)

    def draw_slider(self) -> None:
        self.delete("all")

        # Update positions
        self.start_x = self.val_to_x(self.cursor_start)
        self.end_x = self.val_to_x(self.cursor_end)
        y_center = self.y_padding + self.slider_height / 2

        # Draw the full range line
        self.full_line = self.create_line(
            self.x_padding,
            y_center,
            self.winfo_width() - self.x_padding,
            y_center,
            fill="gray",
            width=self.line_width,
        )

        # Draw the selected range line
        self.range_line = self.create_line(
            self.start_x,
            y_center,
            self.end_x,
            y_center,
            fill="blue",
            width=self.line_width,
        )

        # Draw sliders
        self.start_slider = self.create_rectangle(
            self.start_x - self.slider_width / 2,
            self.y_padding,
            self.start_x + self.slider_width / 2,
            self.y_padding + self.slider_height,
            fill="blue",
            outline="blue",
        )
        self.end_slider = self.create_rectangle(
            self.end_x - self.slider_width / 2,
            self.y_padding,
            self.end_x + self.slider_width / 2,
            self.y_padding + self.slider_height,
            fill="blue",
            outline="blue",
        )

    def val_to_x(self, val: float) -> float:
        return self.x_padding + (val - self.slider_min) / (
            self.slider_max - self.slider_min
        ) * (self.winfo_width() - 2 * self.x_padding)

    def x_to_val(self, x: float) -> float:
        return self.slider_min + (x - self.x_padding) / (
            self.winfo_width() - 2 * self.x_padding
        ) * (self.slider_max - self.slider_min)

    def on_click(self, event) -> None:
        self.prev_mouse_x = event.x
        if self.find_withtag("current"):
            clicked = self.find_closest(event.x, event.y)[0]
            if clicked == self.start_slider:
                self.dragging = "start_slider"
            elif clicked == self.end_slider:
                self.dragging = "end_slider"
            elif self.start_x <= event.x <= self.end_x:
                self.dragging = "range"

    def on_drag(self, event) -> None:
        if self.dragging == "start_slider":
            new_val = self.x_to_val(event.x)
            self.cursor_start = max(
                self.slider_min, min(new_val, self.cursor_end - self.cursor_min_width)
            )
        elif self.dragging == "end_slider":
            new_val = self.x_to_val(event.x)
            self.cursor_end = min(
                self.slider_max, max(new_val, self.cursor_start + self.cursor_min_width)
            )
        elif self.dragging == "range":
            delta_x = event.x - self.prev_mouse_x
            delta_val = (
                delta_x
                / (self.winfo_width() - 2 * self.x_padding)
                * (self.slider_max - self.slider_min)
            )
            new_start_val = self.cursor_start + delta_val
            new_end_val = self.cursor_end + delta_val

            # Ensure range stays within bounds
            if new_start_val < self.slider_min:
                new_start_val = self.slider_min
                new_end_val = new_start_val + (self.cursor_end - self.cursor_start)
            elif new_end_val > self.slider_max:
                new_end_val = self.slider_max
                new_start_val = new_end_val - (self.cursor_end - self.cursor_start)

            self.cursor_start = new_start_val
            self.cursor_end = new_end_val

        self.prev_mouse_x = event.x
        self.update_positions()

    def on_release(self, event) -> None:
        self.dragging = ""
        self.prev_mouse_x = None

    def update_positions(self) -> None:
        self.start_x = self.val_to_x(self.cursor_start)
        self.end_x = self.val_to_x(self.cursor_end)
        y_center = self.y_padding + self.slider_height / 2

        # Update range line
        self.coords(self.range_line, self.start_x, y_center, self.end_x, y_center)

        # Update sliders
        self.coords(
            self.start_slider,
            self.start_x - self.slider_width / 2,
            self.y_padding,
            self.start_x + self.slider_width / 2,
            self.y_padding + self.slider_height,
        )
        self.coords(
            self.end_slider,
            self.end_x - self.slider_width / 2,
            self.y_padding,
            self.end_x + self.slider_width / 2,
            self.y_padding + self.slider_height,
        )

        # Notify callback
        if self.callback:
            self.callback(self.cursor_start, self.cursor_end)

    def on_resize(self, event) -> None:
        # Redraw slider components on resize
        self.draw_slider()


# Main application for test
import numpy as np
import matplotlib.pyplot as plt
from matplotlib.backends.backend_tkagg import FigureCanvasTkAgg


class App:
    def __init__(self, root):
        def _quit() -> None:
            self.root.quit()
            self.root.destroy()

        self.root = root
        self.root.title("Range Slider with Matplotlib")
        self.root.protocol("WM_DELETE_WINDOW", _quit)
        self.setup_ui()

    def setup_ui(self):
        # Create Matplotlib figure
        self.fig, self.ax = plt.subplots(figsize=(6, 4))
        self.x = np.linspace(0, 100, 1000)
        self.y = np.sin(self.x * 2 * np.pi / 100)
        (self.line,) = self.ax.plot(self.x, self.y)
        self.ax.set_xlim(0, 100)

        # Embed Matplotlib in Tkinter
        self.canvas = FigureCanvasTkAgg(self.fig, master=self.root)
        self.canvas_widget = self.canvas.get_tk_widget()
        self.canvas_widget.pack(fill=tk.BOTH, expand=True)

        # # Add Range Slider
        slider = RangeSlider(
            root,
            slider_min=0,
            slider_max=100,
            cursor_start=20,
            cursor_end=80,
            x_padding=10.0,
            y_padding=10.0,
            height=35,
            callback=self.update_plot,
            bg="white",
        )
        slider.pack(fill=tk.X, padx=10, pady=10)
        b_frame = tk.Frame(root)
        b_frame.pack(fill=tk.X, padx=10, pady=10)

        def change_size():
            slider.change_size(10, 90, 10, 90)

        def move_right():
            slider.move_cursor_right(9)

        def move_left():
            slider.move_cursor_left(8)

        def shrink_cursor():
            slider.shrink_cursor()

        def expand_cursor():
            slider.expand_cursor()

        tk.Button(b_frame, text="-", command=shrink_cursor).grid(row=0, column=0)
        tk.Button(b_frame, text="+", command=expand_cursor).grid(row=0, column=1)
        tk.Button(b_frame, text="change size", command=change_size).grid(
            row=0, column=2
        )
        tk.Button(b_frame, text="move left", command=move_left).grid(row=0, column=3)
        tk.Button(b_frame, text="move right", command=move_right).grid(row=0, column=4)

        self.ax.set_xlim(20, 80)

    def update_plot(self, start: float, end: float) -> None:
        self.ax.set_xlim(start, end)
        self.canvas.draw()


if __name__ == "__main__":
    root = tk.Tk()
    app = App(root)
    root.mainloop()
