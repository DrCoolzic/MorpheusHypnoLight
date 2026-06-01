from __future__ import annotations
import tkinter as tk
from tkinter import filedialog, messagebox, ttk
import matplotlib.pyplot as plt
from matplotlib.backends.backend_tkagg import FigureCanvasTkAgg  # type: ignore[misc]
from matplotlib.ticker import AutoMinorLocator, MultipleLocator, FixedLocator
from matplotlib.patches import Rectangle
import matplotlib.figure
import matplotlib.axes as mpl_axes
import matplotlib.lines as mpl_lines
from matplotlib.ticker import FuncFormatter

# from matplotlib.widgets import RangeSlider
import numpy as np
import logging

import rslider, dmutil
# import dmutil, osc, dme, player, rslider

SAMPLES = 8192


class Curve(tk.Frame):
    COLORS = ["blue", "green", "magenta", "red", "yellow", "cyan", "black", "white"]
    LINE_STYLE = ["solid", "dotted", "dashed", "dashdot"]
    MARKERS = ["s", "v", "^", "*"]
    WINDOW_LEN = 1024
    HOP_LEN = 512

    def __init__(self, app: dme.App) -> None:
        """The constructor creates and assemble:
        - curve_frame: all the curves
        - range_frame
        - info_frame: slider and player info
        - control_frame
        """
        super().__init__(app)
        logging.info("Curves constructor start")
        self.app = app
        # self.ble = app.ble
        # self.audio = app.audio
        # self.img = self.app.img

        # def start_stop_playing(event) -> None:
        #     """start/stop playing sequence with space bar"""
        #     if self.app.player.player_state == player.State.PLAYING:
        #         self.app.player.pause_player()
        #     else:
        #         self.app.player.play_player()

        # def on_enter(event) -> None:
        #     # Bind the space key to the frame when the mouse is over it
        #     self.bind("<Key-space>", start_stop_playing)
        #     self.focus_set()

        # def on_leave(event) -> None:
        #     # Unbind the space key from the frame when the mouse leaves
        #     self.unbind("<Key-space>")

        # self.bind("<Key-space>", start_stop_playing)
        # self.bind("<Enter>", on_enter)
        # self.bind("<Leave>", on_leave)

        # self.detail_var = tk.StringVar(value="S")
        # self.osc_sel_var: list[tk.BooleanVar] = list()
        # self.volume_var = tk.DoubleVar(value=0.5)
        # self.timer_id = " "

        self.fig: matplotlib.figure.Figure
        self.canvas: FigureCanvasTkAgg

        self.plot_t_start: float
        self.plot_t_end: float
        self.curve_switches = {
            "Freq": tk.BooleanVar(value=True),
            "Bright": tk.BooleanVar(value=True),
            "Duty": tk.BooleanVar(value=True),
            "Light": tk.BooleanVar(value=True),
            "Sound": tk.BooleanVar(value=True),
        }
        self.curve_axs: dict[str, mpl_axes.Axes]
        self.cursors: dict[str, mpl_lines.Line2D]

        self.canvas_frame = tk.Frame(self)
        self.create_canvas()  # we create an empty canvas
        self.canvas_frame.grid(row=0, column=0, sticky="news", padx=3)

        self.range_frame = tk.Frame(self)
        self.range_frame.grid(row=1, column=0, sticky="swe", padx=3)

        self.info_frame = tk.Frame(self)
        self.info_frame.grid(row=2, column=0, sticky="swe", padx=3)

        self.control_frame = tk.Frame(self)
        self.control_frame.grid(row=3, column=0, sticky="we", padx=3)

        self.columnconfigure(0, weight=1)
        self.rowconfigure(0, weight=1)

        """range_frame elements"""
        self.range_slider = rslider.RangeSlider(
            self.range_frame,
            slider_min=0,
            slider_max=10,
            cursor_start=0,
            cursor_end=10,
            x_padding=10.0,
            y_padding=5.0,
            height=25,
            callback=self.plot_range_changed,
            bg="white",
        )
        tk.Button(
            self.range_frame, text="-", width=3, command=self.range_slider.expand_cursor
        ).grid(row=0, column=0)
        tk.Button(
            self.range_frame, text="+", width=3, command=self.range_slider.shrink_cursor
        ).grid(row=0, column=1)
        self.range_slider.grid(row=0, column=2, sticky="ew")
        self.range_frame.columnconfigure(2, weight=1)

        """ info_frame elements """
        tk.Label(self.info_frame, text="Cursor").grid(
            row=0, column=0, sticky="w", padx=2, pady=2
        )
        self.cur_pos_lbl = tk.Label(self.info_frame, width=4, text="0", bg="#eee")
        self.cur_pos_lbl.grid(row=0, column=1, sticky="w", padx=2, pady=2)

        tk.Label(self.info_frame, text="Step").grid(
            row=0, column=2, sticky="w", padx=2, pady=2
        )
        self.cur_step_lbl = tk.Label(self.info_frame, width=5, text="0/0", bg="#eee")
        self.cur_step_lbl.grid(row=0, column=3, sticky="w", padx=2, pady=2)

        self.osc_values_lbl = tk.Label(self.info_frame, width=80, bg="#eee", anchor="w")
        self.osc_values_lbl.grid(row=0, column=4, sticky="we", padx=3, pady=3)
        self.info_frame.columnconfigure(4, weight=1)

        self.cursor_xy_lbl = tk.Label(self.info_frame, bg="#eee", width=20, anchor="w")
        self.cursor_xy_lbl.grid(row=0, column=5, sticky="w", padx=2, pady=2)

        tk.Label(self.info_frame, text="Seq dur:").grid(
            row=0, column=6, sticky="w", padx=2, pady=2
        )
        self.seq_dur_lbl = tk.Label(self.info_frame, width=6, text="0", bg="#eee")
        self.seq_dur_lbl.grid(row=0, column=7, sticky="e", padx=3, pady=3)
        tk.Label(self.info_frame, text="Audio dur:").grid(
            row=0, column=8, sticky="e", padx=1, pady=3
        )
        self.seq_audio_lbl = tk.Label(self.info_frame, width=6, bg="#eee", text="----")
        self.seq_audio_lbl.grid(row=0, column=9, sticky="e", padx=2, pady=3)

        """ control_frame:
            - play_ctrl_frame: play stop buttons & volume
            - select_frame: select Freq, Bright, Duty, Light, Sound
            - detail_frame: select Signal or Envelope
            - osc_frame: select oscillators to display
        """
        self.play_ctrl_frame = tk.LabelFrame(self.control_frame, text="Player Control")
        self.play_ctrl_frame.grid(row=0, column=0, padx=3, pady=3, sticky="ew")
        select_frame = tk.LabelFrame(self.control_frame, text="Curves selection")
        select_frame.grid(row=0, column=2, padx=3)
        detail_frame = tk.LabelFrame(self.control_frame, text="Light display")
        detail_frame.grid(row=0, column=3, padx=3)
        osc_frame = tk.LabelFrame(self.control_frame, text="Oscillator display")
        osc_frame.grid(row=0, column=4, padx=3)
        self.control_frame.columnconfigure(0, weight=1)
        tk.Button(
            self.play_ctrl_frame,
            # image=self.img.play_img,
            # command=self.app.player.play_player,
        ).grid(row=0, column=0, pady=3, sticky="w")
        tk.Button(
            self.play_ctrl_frame,
            # image=self.img.pause_img,
            # command=self.app.player.pause_player,
        ).grid(row=0, column=1, pady=3, sticky="w")
        tk.Button(
            self.play_ctrl_frame,
            # image=self.img.stop_img,
            # command=self.app.player.stop_player,
        ).grid(row=0, column=2, pady=3, sticky="w")
        tk.Button(
            self.play_ctrl_frame,
            # image=self.img.step_img_previous,
            # command=self.app.param.prev_step,
        ).grid(row=0, column=3, pady=3, sticky="w")
        tk.Button(
            self.play_ctrl_frame,
            # image=self.img.step_img_next,
            # command=self.app.param.next_step,
        ).grid(row=0, column=4, pady=3, sticky="w")
        ttk.Label(self.play_ctrl_frame, text="Volume").grid(
            row=0, column=5, padx=3, sticky="w"
        )
        self.volume_slider = ttk.Scale(
            self.play_ctrl_frame,
            from_=0.0,
            to=1.0,
            orient="horizontal",
            # variable=self.volume_var,
            command=self.set_volume,
        )
        self.volume_slider.grid(row=0, column=6, sticky="we", padx=3)
        self.play_ctrl_frame.columnconfigure(6, weight=1)

        """ select_frame elements """
        for i, curve in enumerate(self.curve_switches):
            tk.Checkbutton(
                select_frame,
                text=curve[0:1],
                # style="Switch.TCheckbutton",
                variable=self.curve_switches[curve],
                command=lambda name=curve, var=self.curve_switches[curve]: curve_cb_changed(  # type: ignore[misc]
                    name, var
                ),
            ).grid(row=0, column=i, pady=3)

        """ detail_frame elements """
        tk.Radiobutton(
            detail_frame,
            text="Sig",
            # variable=self.detail_var,
            command=self.plot_sequence,
            value="S",
        ).grid(row=0, column=0, pady=3)
        tk.Radiobutton(
            detail_frame,
            text="Env",
            value="E",
            # variable=self.detail_var,
            command=self.plot_sequence,
        ).grid(row=0, column=1)

        # """ osc_frame elements """
        # for i in range(4):
        #     self.osc_sel_var.append(tk.BooleanVar(value=True))
        #     tk.Checkbutton(
        #         osc_frame,
        #         text=f"{i}",
        #         variable=self.osc_sel_var[i],
        #         command=self.plot_sequence,
        #     ).grid(row=0, column=i, pady=3)

        def curve_cb_changed(curve_name: str, curve_var: tk.BooleanVar) -> None:
            self.create_canvas()
            self.plot_sequence()

        # initially disable all widgets
        dmutil.enable_children(self, False)
        self.range_slider.grid_forget()

    def on_click(self, event) -> None:
        # Check if the click was inside the plot area
        # if event.inaxes == self.ax:
        x = event.xdata
        if x and self.app.player.player_state != player.State.PLAYING:
            self.set_cursor_pos(int(x))
            if event.button == 3:  # right click
                self.app.split_step(x)

    def on_motion(self, event):
        """Display the coordinates of the cursor in the label."""
        # if event.inaxes:  # Check if the cursor is inside the axes
        #     coords_label.config(text=f"X: {event.xdata:.2f}, Y: {event.ydata:.2f}")
        # else:
        #     coords_label.config(text="Out of bounds")
        if event.xdata:
            x = round(event.xdata)
            y = round(event.ydata, 1)
            mess = f"XY= {x}({dmutil.fmt_time(x)}), {y}"
        else:
            mess = "-----"
        self.cursor_xy_lbl.config(text=mess)

    def set_volume(self, value: str) -> None:
        """Set the volume of the audio from the slider value"""
        self.audio.volume = self.volume_var.get()

    def display_osc_cur_value(self, position) -> None:
        step_at_pos, _, osc_values = dmutil.osc_values_at_pos(
            position, self.app.sequence
        )
        values = str()
        for i in range(4):
            if osc_values[i][0] != -1:
                values += f"Osc_{i}=F{dmutil.float2str(osc_values[i][0])}-B{dmutil.float2str(osc_values[i][1])}-D{dmutil.float2str(osc_values[i][2])}  "
        self.osc_values_lbl.config(text=values)

    def set_cursor_pos(self, position: int) -> None:
        """
        - set app cursor_pos
        - activate step if different
        - move the position of the cursors on all curves
        - the current_play_time
        - the play_slider_pos
        - the osc_values_lbl
        - set player start position
        - move slider if necessary
        """
        self.app.cursor_pos = position
        # active step if different
        step_at_pos, _ = dmutil.in_step_pos(position, self.app.sequence)
        if self.app.activated_step != step_at_pos:
            self.app.activate_step(step_at_pos)

        # move cursors on curves
        for curve in self.curve_switches:
            if self.switch(curve):
                self.cursors[curve].set_xdata([position])
        self.fig.canvas.draw()

        self.cur_pos_lbl.config(text=position)
        self.display_osc_cur_value(position)
        self.app.player.start_position = position

        # move range-slider if necessary
        if position < self.range_slider.cursor_start:
            self.range_slider.move_cursor_left(
                self.range_slider.cursor_start - position
            )
        range_duration = self.range_slider.cursor_end - self.range_slider.cursor_start
        if position > self.range_slider.cursor_start + (0.9 * range_duration):
            shift = 0.8 * range_duration
            self.range_slider.move_cursor_right(shift)

    def set_player_duration(self, duration: int) -> None:
        self.seq_dur_lbl.config(text=duration)

    def switch(self, name: str) -> bool:
        """Return boolean value for curve switch except for the Sound where the presence of a sound file is also checked"""
        if name == "Sound":
            return self.curve_switches["Sound"].get() # and self.app.has_sound
        else:
            return self.curve_switches[name].get()

    def create_canvas(self) -> None:
        """
        - Creates a canvas in the Curve frame the first time it is called.
          In subsequent call we delete the plots and widgets in the canvas.
        - Creates a figure that contains as many subplots as curves requested
        - The axes are entered in curve_axs list and default param are set
        - The figure is added to a canvas and drawn.
        - NO plots are created by this function
        """
        logging.info("Creating the canvas and figure")
        if hasattr(self, "canvas"):  # we already have the canvas
            self.fig.clear()  # remove plots
            self.canvas.get_tk_widget().pack_forget()  # remove widgets
            self.canvas.get_tk_widget().destroy()

        # create the Figure and all Axes
        self.curve_axs = dict()  # reset the dictionary of axes
        self.cursors = dict()  # reset the dictionary of cursors
        curve_cnt = 0
        for curve in self.curve_switches:
            curve_cnt += self.switch(curve)
        if curve_cnt: # and self.app.initialized:
            self.fig, axs = plt.subplots(curve_cnt, figsize=(13, 5))
            self.fig.set_tight_layout(True)  # type: ignore

            first = True
            for curve in self.curve_switches:  # check curves
                if self.switch(curve):  # curve switch is on
                    self.curve_axs[curve] = (
                        axs[len(self.curve_axs)] if isinstance(axs, np.ndarray) else axs
                    )  # add the curve ax to the dictionary
                    self.curve_axs[curve].grid(
                        visible=True, which="major", axis="both", lw=1, color="#88f"
                    )
                    self.curve_axs[curve].grid(
                        visible=True, which="minor", axis="y", lw=0.5, color="#ddd"
                    )
                    self.curve_axs[curve].yaxis.set_minor_locator(AutoMinorLocator())

                    # add a legend on the first curve
                    if first and (curve != "Light") and (curve != "Sound"):
                        first = False
                        proxy_artists = list()
                        for x in range(4):
                            proxy_artists += plt.plot(
                                [],
                                [],
                                Curve.MARKERS[x],
                                c=Curve.COLORS[x],
                                lw=2,
                                ls=Curve.LINE_STYLE[x],
                            )
                        self.curve_axs[curve].legend(
                            handles=proxy_artists,
                            labels=[f"osc {i+1}" for i in range(4)],
                        )

        else:  # no curve => we create an empty plot
            self.fig = plt.figure(figsize=(13, 5))

        # add the canvas to the canvas_frame
        self.canvas = FigureCanvasTkAgg(self.fig, master=self.canvas_frame)
        self.canvas.draw()
        self.canvas.get_tk_widget().pack(expand=1, fill="both")
        self.canvas.mpl_connect("button_press_event", self.on_click)
        self.canvas.mpl_connect("motion_notify_event", self.on_motion)

    def plot_range_changed(self, start, end):
        """The start/stop positions have changed: We only update the plots x limits"""
        # logging.info(f"start={start} end={end} changed updating plot")
        self.plot_t_start = start
        self.plot_t_end = end
        for curve in self.curve_switches:
            if not self.switch(curve):
                continue
            self.curve_axs[curve].set_xlim(start, end)
        self.canvas.draw()

    def plot_sequence(self) -> None:
        """Plot the sequence. Either on new/open or on sequence duration change
        - we initialize the start, end values for the plot
        - we set the range slider size and cursor position
        - we change the grid values based on current info
        - we plot all curves
        """
        self.plot_t_start = self.app.sequence.steps[0].t_start
        self.plot_t_end = self.app.sequence.steps[-1].t_end
        logging.info(f"Drawing all plots from {self.plot_t_start} to {self.plot_t_end}")
        self.range_slider.change_size(
            slider_min=self.app.sequence.steps[0].t_start,
            slider_max=self.app.sequence.steps[-1].t_end,
            cursor_start=self.app.sequence.steps[0].t_start,
            cursor_end=self.app.sequence.steps[-1].t_end,
        )

        # we draw x axis major and minor ticks
        shift = (self.plot_t_end - self.plot_t_start) / 4000
        x_step_ticks = [step.t_start + shift for step in self.app.sequence.steps]
        x_step_ticks.append(self.app.sequence.steps[-1].t_end)  # add last
        for curve in self.curve_switches:
            if self.switch(curve):
                if curve == "Sound" and not np.any(self.app.audio.samples):
                    continue
                self.curve_axs[curve].tick_params(
                    axis="both", which="major", colors="blue"
                )
                self.curve_axs[curve].xaxis.set_minor_locator(
                    FixedLocator(x_step_ticks)
                )
                self.curve_axs[curve].grid(
                    visible=True, which="minor", axis="x", lw=2, color="#000"
                )
                # self.curve_axs[curve].xaxis.set_major_formatter(
                #     FuncFormatter(time_format)
                # )

        self.draw_freq()
        self.draw_bright()
        self.draw_light()
        self.draw_duty()
        self.draw_audio()

    def osc_changed(self) -> None:
        """Some osc parameter changed
        - we plot all curves except the sound (not changed)
        """
        logging.info(f"Oscillator changed need to redraw DM plots")
        self.draw_freq()
        self.draw_bright()
        self.draw_light()
        self.draw_duty()

    def led_power(self, led_list: list[str]) -> float:
        """Return a power factor proportional to LED used"""
        power = 0.0
        for led in led_list:
            led = "A2" if led == "A3" else led
            if led == "B5":
                continue
            power += osc.Osc.LEDS[led]
        return power / 21

    def compute_light(self) -> None:
        """this function will update the information related to light"""
        self.x_light: np.ndarray = np.empty((0))
        self.y_light: np.ndarray = np.empty((0))
        prev_x_light = np.empty(0)
        prev_y_light = np.empty(0)

        # compute x,y values for current step
        for step_idx, step in enumerate(self.app.sequence.steps):
            # samples = max(1000, 200 * (step.t_end - step.t_start))
            x_light = dmutil.linear_gen(step.t_start, step.t_end, SAMPLES)
            y_light = np.zeros(SAMPLES)  # init
            for i, osc in enumerate(step.oscillators):
                if not (self.osc_sel_var[i].get() and osc.leds):
                    continue  # skip if switch is off or empty
                duty = dmutil.linear_gen(osc.d_start / 100, osc.d_end / 100, SAMPLES)
                bright = dmutil.linear_gen(
                    osc.b_start, osc.b_end, SAMPLES
                ) * self.led_power(osc.leds)
                y_light += (
                    dmutil.pwm_gen(x_light, osc.f_start, osc.f_end, duty) * bright
                )

            cur_start_idx = 0  # from first
            cur_end_idx = -1  # to last

            # if the current step is overlapping with the previous one we set current_start_idx and we merge with saved
            if step_idx > 0:
                overlap = self.app.sequence.steps[step_idx - 1].t_end - step.t_start
                if overlap > 0:
                    cur_duration = step.t_end - step.t_start
                    cur_ss = SAMPLES // cur_duration
                    cur_start_idx = (overlap) * cur_ss
                    prev_duration = (
                        self.app.sequence.steps[step_idx - 1].t_end
                        - self.app.sequence.steps[step_idx - 1].t_start
                    )
                    prev_ss = SAMPLES // prev_duration
                    prev_end_idx = (prev_duration - overlap) * prev_ss
                    # print(
                    #     f"step={step_idx} {overlap=} with prev {cur_duration=} {cur_ss=}  merging current [0,{cur_start_idx}]\n"
                    #     + f"                           {prev_duration=} {prev_ss=} merging prev [{prev_end_idx}:-1]"
                    # )
                    temp_x = np.empty(prev_x_light.shape[0] - prev_end_idx)
                    temp_y = np.empty(prev_x_light.shape[0] - prev_end_idx)

                    # merging the two overlapping segments and concatenate
                    for i in range(prev_end_idx, prev_x_light.shape[0]):
                        j = int((i - prev_end_idx) * cur_ss / prev_ss)
                        temp_x[i - prev_end_idx] = prev_x_light[i]
                        # we take the max of previous and current
                        temp_y[i - prev_end_idx] = max(prev_y_light[i], y_light[j])
                    self.x_light = np.concatenate((self.x_light, temp_x), axis=0)
                    self.y_light = np.concatenate((self.y_light, temp_y), axis=0)

            # if the current step is overlapping with current one we set cur_end_idx and we save current values
            if step_idx < len(self.app.sequence.steps) - 1:
                overlap = step.t_end - self.app.sequence.steps[step_idx + 1].t_start
                if overlap > 0:
                    cur_duration = step.t_end - step.t_start
                    cur_ss = SAMPLES // cur_duration  # samples/second
                    cur_end_idx = (cur_duration - overlap) * cur_ss
                    # print(
                    #     f"step={step_idx} {overlap=} with next {cur_duration=} {cur_ss=} {cur_end_idx=} saving data"
                    # )
                    prev_x_light = x_light
                    prev_y_light = y_light

            # now we concatenate current computation from cur_start_idx to cur_end_idx
            self.x_light = np.concatenate(
                (self.x_light, x_light[cur_start_idx:cur_end_idx]), axis=0
            )
            self.y_light = np.concatenate(
                (self.y_light, y_light[cur_start_idx:cur_end_idx]), axis=0
            )

    def draw_light(self) -> None:
        """draw the light curve"""
        if not self.curve_switches["Light"].get():
            return
        for line in self.curve_axs["Light"].lines:
            line.remove()  # remove old plot
        for patch in self.curve_axs["Light"].patches:
            patch.remove()

        self.compute_light()  # (re)compute the light curve
        y_min = min(self.y_light) - 0.5
        y_max = max(self.y_light) + 0.5

        cursor = self.curve_axs["Light"].axvline(
            self.app.cursor_pos, c="k", ls="--", zorder=20
        )
        self.cursors["Light"] = cursor

        self.curve_axs["Light"].set_xlim(self.plot_t_start, self.plot_t_end)
        self.curve_axs["Light"].set_ylabel("Light")
        self.curve_axs["Light"].set_ylim(y_min, y_max)

        if self.detail_var.get() == "S":
            self.curve_axs["Light"].plot(self.x_light, self.y_light, lw=0.5, c="b")
        else:
            env = dmutil.env(
                self.y_light,
                frame_length=Curve.WINDOW_LEN,
                hop_length=Curve.HOP_LEN,
            )
            self.curve_axs["Light"].plot(
                self.x_light[:: -Curve.HOP_LEN], env[::-1], c="r"
            )
            rms = dmutil.rms(
                self.y_light,
                frame_length=Curve.WINDOW_LEN,
                hop_length=Curve.HOP_LEN,
            )
            self.curve_axs["Light"].plot(
                self.x_light[:: -Curve.HOP_LEN], rms[::-1], c="black"
            )

        self.curve_axs["Light"].add_patch(
            Rectangle(
                (self.app.sequence.steps[self.app.activated_step].t_start, y_min),
                self.app.sequence.steps[self.app.activated_step].t_end
                - self.app.sequence.steps[self.app.activated_step].t_start,
                y_max - y_min,
                facecolor="#eed",
            )
        )
        self.canvas.draw_idle()

    def draw_freq(self) -> None:
        """draw the frequency curve"""
        if not self.curve_switches["Freq"].get():
            return
        for line in self.curve_axs["Freq"].lines:
            line.remove()  # remove old plot
        for patch in self.curve_axs["Freq"].patches:
            patch.remove()

        self.curve_axs["Freq"].set_xlim(self.plot_t_start, self.plot_t_end)
        self.curve_axs["Freq"].set_ylabel("Frequency")

        f_min = 1000.0
        f_max = 0.0
        for step in self.app.sequence.steps:
            for osc_id, osc in enumerate(step.oscillators):
                if not (self.osc_sel_var[osc_id].get() and osc.leds):
                    continue
                f_min = min(f_min, osc.f_start, osc.f_end)
                f_max = max(f_max, osc.f_start, osc.f_end)
                self.curve_axs["Freq"].plot(
                    [
                        step.t_start,
                        step.t_end,
                    ],
                    [osc.f_start, osc.f_end],
                    Curve.MARKERS[osc_id],
                    c=Curve.COLORS[osc_id],
                    alpha=0.8,
                    ls=Curve.LINE_STYLE[osc_id],
                    lw=2,
                )

        f_min = f_min - 0.5
        f_max = f_max + 0.5

        cursor = self.curve_axs["Freq"].axvline(
            self.app.cursor_pos, c="k", ls="--", zorder=20
        )
        self.cursors["Freq"] = cursor  # save for draw_cursor

        self.curve_axs["Freq"].add_patch(
            Rectangle(
                (self.app.sequence.steps[self.app.activated_step].t_start, f_min),
                self.app.sequence.steps[self.app.activated_step].t_end
                - self.app.sequence.steps[self.app.activated_step].t_start,
                f_max - f_min,
                facecolor="#eed",
            )
        )
        self.curve_axs["Freq"].set_ylim(f_min, f_max)
        self.canvas.draw_idle()

    def draw_bright(self) -> None:
        """draw the brightness curve"""
        if not self.curve_switches["Bright"].get():
            return
        for line in self.curve_axs["Bright"].lines:
            line.remove()  # remove old plot
        for patch in self.curve_axs["Bright"].patches:
            patch.remove()

        self.curve_axs["Bright"].set_xlim(self.plot_t_start, self.plot_t_end)
        self.curve_axs["Bright"].set_ylabel("Brightness")

        b_min = 100.0
        b_max = 0.0
        for step in self.app.sequence.steps:
            for osc_id, osc in enumerate(step.oscillators):
                if not (self.osc_sel_var[osc_id].get() and osc.leds):
                    continue
                b_min = min(b_min, osc.b_start, osc.b_end)
                b_max = max(b_max, osc.b_start, osc.b_end)
                self.curve_axs["Bright"].plot(
                    [
                        step.t_start,
                        step.t_end,
                    ],
                    [osc.b_start, osc.b_end],
                    Curve.MARKERS[osc_id],
                    c=Curve.COLORS[osc_id],
                    alpha=0.8,
                    ls=Curve.LINE_STYLE[osc_id],
                    lw=2,
                )
        b_min = b_min - 0.5
        b_max = b_max + 0.5

        cursor = self.curve_axs["Bright"].axvline(
            self.app.cursor_pos, c="k", ls="--", zorder=20
        )
        self.cursors["Bright"] = cursor  # save for draw_cursor

        self.curve_axs["Bright"].add_patch(
            Rectangle(
                (self.app.sequence.steps[self.app.activated_step].t_start, b_min),
                self.app.sequence.steps[self.app.activated_step].t_end
                - self.app.sequence.steps[self.app.activated_step].t_start,
                100,
                facecolor="#eed",
            )
        )
        self.curve_axs["Bright"].set_ylim(b_min, b_max)
        self.canvas.draw_idle()

    def draw_duty(self) -> None:
        """draw the duty cycle curve"""
        if not self.curve_switches["Duty"].get():
            return
        for line in self.curve_axs["Duty"].lines:
            line.remove()  # remove old plot
        for patch in self.curve_axs["Duty"].patches:
            patch.remove()

        self.curve_axs["Duty"].set_xlim(self.plot_t_start, self.plot_t_end)
        self.curve_axs["Duty"].set_ylabel("Duty")

        d_min = 100.0
        d_max = 0.0
        for step in self.app.sequence.steps:
            for osc_id, osc in enumerate(step.oscillators):
                if not (self.osc_sel_var[osc_id].get() and osc.leds):
                    continue
                d_min = min(d_min, osc.d_start, osc.d_end)
                d_max = max(d_max, osc.d_start, osc.d_end)
                self.curve_axs["Duty"].plot(
                    [
                        step.t_start,
                        step.t_end,
                    ],
                    [osc.d_start, osc.d_end],
                    Curve.MARKERS[osc_id],
                    c=Curve.COLORS[osc_id],
                    alpha=0.8,
                    ls=Curve.LINE_STYLE[osc_id],
                    lw=2,
                )
        d_min = d_min - 0.5
        d_max = d_max + 0.5

        cursor = self.curve_axs["Duty"].axvline(
            self.app.cursor_pos, c="k", ls="--", zorder=20
        )
        self.cursors["Duty"] = cursor  # save for draw_cursor

        self.curve_axs["Duty"].add_patch(
            Rectangle(
                (self.app.sequence.steps[self.app.activated_step].t_start, d_min),
                self.app.sequence.steps[self.app.activated_step].t_end
                - self.app.sequence.steps[self.app.activated_step].t_start,
                d_max + 1 - d_min - 1,
                facecolor="#eed",
            )
        )
        self.curve_axs["Duty"].set_ylim(d_min, d_max)
        self.canvas.draw_idle()

    def callback_audio(self):
        """ "This is called by the read audio async thread to avoid synch problem"""
        self.app.audio_ready = True
        self.app.after(0, self.draw_audio)

    def draw_audio(self):
        """draw the audio curve"""

        if not self.switch("Sound"):
            return
        if not np.any(self.app.audio.samples):
            logging.info(f"Drawing audio no samples({np.any(self.app.audio.samples)})")
            self.cursors["Sound"] = self.curve_axs["Sound"].axvline(0)
            return
        else:
            logging.info(f"Drawing the audio")

        for line in self.curve_axs["Sound"].lines:
            line.remove()  # remove old plot
        for patch in self.curve_axs["Sound"].patches:
            patch.remove()

        self.curve_axs["Sound"].set_xlim(self.plot_t_start, self.plot_t_end)
        self.curve_axs["Sound"].set_ylabel("Sound")

        min = np.ndarray.min(self.app.audio.right_channel)
        max = np.ndarray.max(self.app.audio.left_channel)

        cursor = self.curve_axs["Sound"].axvline(
            self.app.cursor_pos, c="k", ls="--", zorder=20
        )
        self.cursors["Sound"] = cursor  # save for draw_cursor

        # Calculate the number of samples corresponding to the display duration
        # Assume waveform is linearly distributed over its duration
        waveform_num_samples = len(self.app.audio.left_channel)
        display_length_in_seconds = self.app.sequence.duration
        waveform_length_in_seconds = self.app.audio.duration
        # self.seq_audio_lbl.config(text=dmutil.fmt_time(waveform_length_in_seconds))
        self.seq_audio_lbl.config(text=int(waveform_length_in_seconds))
        num_samples_display = int(
            (waveform_num_samples / waveform_length_in_seconds)
            * display_length_in_seconds
        )
        # If the waveform is longer, it is truncated; if it is shorter, it is completed with zeros.
        if waveform_num_samples > num_samples_display:
            left = self.app.audio.left_channel[:num_samples_display]
            right = self.app.audio.right_channel[:num_samples_display]
        else:
            left = np.pad(
                self.app.audio.left_channel,
                (0, num_samples_display - waveform_num_samples),
                "constant",
            )
            right = np.pad(
                self.app.audio.right_channel,
                (0, num_samples_display - waveform_num_samples),
                "constant",
            )
        final_num_samples = len(left)
        time = np.linspace(0, display_length_in_seconds, final_num_samples)

        self.curve_axs["Sound"].set_xlim(self.plot_t_start, self.plot_t_end)
        if self.app.audio.ch == 2:
            left[np.where(left < 0)] = 0
            self.curve_axs["Sound"].plot(time, left, lw=0.6, c="b")
            right[np.where(right > 0)] = 0
            self.curve_axs["Sound"].plot(time, right, lw=0.6, c="r")
            self.curve_axs["Sound"].set_ylim(min, max)
        else:
            self.curve_axs["Sound"].plot(time, left, lw=0.6, c="b")
            self.curve_axs["Sound"].set_ylim(min, max)
        # self.curve_axs["Sound"].legend(loc="best")
        self.curve_axs["Sound"].get_yaxis().set_ticklabels([])
        height = float(max) - float(min)
        self.curve_axs["Sound"].add_patch(
            Rectangle(
                (self.app.sequence.steps[self.app.activated_step].t_start, min),
                self.app.sequence.steps[self.app.activated_step].t_end
                - self.app.sequence.steps[self.app.activated_step].t_start,
                height,
                facecolor="#eed",
            )
        )
        self.canvas.draw_idle()

    def update_active_region(self) -> None:
        """draw the active rectangle on all curve"""
        for curve in self.curve_switches:  # check curves
            if self.switch(curve):  # curve switch is on
                for patch in self.curve_axs[curve].patches:
                    patch.remove()
                min, max = self.curve_axs[curve].get_ylim()
                self.curve_axs[curve].add_patch(
                    Rectangle(
                        (self.app.sequence.steps[self.app.activated_step].t_start, min),
                        self.app.sequence.steps[self.app.activated_step].t_end
                        - self.app.sequence.steps[self.app.activated_step].t_start,
                        max - min,
                        facecolor="#eed",
                    )
                )
                self.canvas.draw_idle()
