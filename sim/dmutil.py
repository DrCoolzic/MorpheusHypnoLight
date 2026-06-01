from __future__ import annotations
import numpy as np
from typing import Callable
from PIL import Image, ImageTk
# import seq, dme
import sim


# def remove_special_char(text):
#     """Removes all unwanted char in the directory name"""
#     return "".join([i for i in text if ((ord(i) == 32) or (48 < ord(i) < 122))])


float2str: Callable[[float], str] = lambda value: (
    str(value).split(".")[0] if value.is_integer() else str(round(value, 1))
)
"""convert a float to an int or float string"""

float2int: Callable[[float], int | float] = lambda value: (
    int(str(value).split(".")[0]) if value.is_integer() else value
)
"""try to convert a float to an int"""


def s2num(value: str) -> int | float:
    # to output int of float number directly: eg 23 or 23.1
    if float(value).is_integer():
        return int(value.split(".")[0])
    else:
        return float(value)


linear_gen: Callable[[float, float, int], np.ndarray] = (
    lambda start, end, samples: np.linspace(start, end, samples)
)
"""generate an array of n <samples> ranging linearly from <start> to <end>"""


def linear_value(at_time: int, start: float, end: float, duration: float) -> float:
    """return the value of a linear signal at a given time"""
    return start + (end - start) * (at_time / duration)


# def in_step_pos(at_time: float, seq: seq.Sequence) -> tuple[int, int]:
#     if at_time > seq.duration:
#         return (-1, 0)
#     for step_index, step in enumerate(seq.steps):
#         if step.t_start <= at_time < step.t_end:
#             pos_in_step = round(at_time - step.t_start)
#             return (step_index, pos_in_step)
#         else:
#             continue
#     return (-1, 0)


# def osc_values_at_pos(
#     at_time: float, seq: seq.Sequence
# ) -> tuple[int, int, list[tuple[float, float, float]]]:
#     """Given a time position we compute:
#     - the step at this time
#     - the position inside this step
#     - the oscillators value at this time"""
#     osc_values = [
#         (-1.0, 0.0, 0.0),
#         (-1.0, 0.0, 0.0),
#         (-1.0, 0.0, 0.0),
#         (-1.0, 0.0, 0.0),
#     ]
#     step_index, pos_in_step = in_step_pos(at_time, seq)
#     duration = seq.steps[step_index].t_end - seq.steps[step_index].t_start
#     if step_index == -1:
#         print(f"{step_index=} at {at_time=}")
#         return step_index, pos_in_step, osc_values
#     for osc_idx, osc in enumerate(seq.steps[step_index].oscillators):
#         if osc.leds == []:  # filter osc with no led
#             continue
#         f_value = round(linear_value(pos_in_step, osc.f_start, osc.f_end, duration), 1)
#         b_value = round(linear_value(pos_in_step, osc.b_start, osc.b_end, duration), 1)
#         d_value = round(linear_value(pos_in_step, osc.d_start, osc.d_end, duration), 1)
#         osc_values[osc_idx] = (f_value, b_value, d_value)
#     return step_index, pos_in_step, osc_values


def pwm_gen(t: np.ndarray, f0: float, f1: float, d: np.ndarray) -> np.ndarray:
    """frequency-swept and duty_cycle-swept pulse width generator:
    - where " f0*t + 0.5*((f1-f0)/t[-1])*t*t " is the integral of " f0+(f1-f0)/t[-1])*t "
    from t[0] to t[-1]"""
    return np.array(
        [
            1 if p % 1 < d[i] else 0
            for i, p in enumerate(f0 * t + 0.5 * ((f1 - f0) / t[-1]) * t * t)
        ]
    )


# https://stackoverflow.com/questions/24942760
def enable_children(parent, enabled=True):
    for child in parent.winfo_children():
        w_type = child.winfo_class()
        # print(f"{w_type=} {"enabled" if enabled else "disabled"}")
        if w_type not in ("Frame", "Labelframe", "TFrame", "TLabelframe", "Canvas"):
            child.configure(state="normal" if enabled else "disabled")
        else:
            enable_children(child, enabled)


def enable_widget(widget, enabled=True):
    w_type = widget.winfo_class()
    if w_type not in ("Frame", "Labelframe", "TFrame", "TLabelframe", "Canvas"):
        widget.configure(state="normal" if enabled else "disabled")
    else:
        enable_children(widget, enabled)


def fmt_time(seconds: float) -> str:
    minutes = int(seconds // 60)
    seconds = int(seconds % 60)
    return f"{minutes}:{seconds:02}"


# def fmt_time(sec):
#     """Convert seconds into HH:MM:SS format."""
#     hours, remainder = divmod(sec, 3600)
#     minutes, secs = divmod(remainder, 60)
#     # return f"{hours:02}:{minutes:02}:{secs:02}"
#     return f"{sec} ({minutes:02}:{secs:02})"


# def hilbert_envelopes_idx(s, dmin=1, dmax=1, split=False):
#     """Hilbert envelope index finder

#     Input :
#     - s: 1d-array, data signal from which to extract high and low envelopes
#     - dmin, dmax: int, optional, size of chunks, use this if the size of the input signal is too big
#     - split: bool, optional, if True, split the signal in half along its mean, might help to generate the envelope in some cases

#     Output :
#     - lmin,lmax : high/low envelope idx of input signal s"""

#     # locals min
#     lmin = (np.diff(np.sign(np.diff(s))) > 0).nonzero()[0] + 1
#     # locals max
#     lmax = (np.diff(np.sign(np.diff(s))) < 0).nonzero()[0] + 1

#     if split:
#         # s_mid is zero if s centered around x-axis or more generally mean of signal
#         s_mid = np.mean(s)
#         # pre-sorting of locals min based on relative position with respect to s_mid
#         lmin = lmin[s[lmin] < s_mid]
#         # pre-sorting of local max based on relative position with respect to s_mid
#         lmax = lmax[s[lmax] > s_mid]

#     # global min of dmin-chunks of locals min
#     lmin = lmin[
#         [i + np.argmin(s[lmin[i : i + dmin]]) for i in range(0, len(lmin), dmin)]
#     ]
#     # global max of dmax-chunks of locals max
#     lmax = lmax[
#         [i + np.argmax(s[lmax[i : i + dmax]]) for i in range(0, len(lmax), dmax)]
#     ]
#     return lmin, lmax


# def env(signal, frame_length=1024, hop_length=512):
#     return np.array(
#         [max(signal[i : i + frame_length]) for i in range(0, len(signal), hop_length)]
#     )


# def rms(signal, frame_length=1024, hop_length=512):
#     rms = []
#     for i in range(0, len(signal), hop_length):
#         rms_cur_frame = np.sqrt(
#             np.sum(signal[i : i + frame_length] ** 2) / frame_length
#         )
#         rms.append(rms_cur_frame)
#     return np.array(rms)


# def frames_to_samples(frames, *, hop_length=512):
#     """Convert frame indices to audio sample indices.
#     return (np.asanyarray(frames) * hop_length).astype(int)


# def samples_to_time(samples, *, sr=22050):
#     """Convert sample indices to time (in seconds).
#     return np.asanyarray(samples) / float(sr)


def frames_to_time(frames, *, sr=22050, hop_length=512):
    """Convert frame counts to time (seconds).

    Parameters
    ----------
    frames : np.ndarray [shape=(n,)]
        frame index or vector of frame indices
    sr : number > 0 [scalar]
        audio sampling rate
    hop_length : int > 0 [scalar]
        number of samples between successive frames

    Returns
    -------
    times : np.ndarray [shape=(n,)]
        time (in seconds) of each given frame number::

            times[i] = frames[i] * hop_length / sr
    """
    samples = (np.asanyarray(frames) * hop_length).astype(int)
    return np.asanyarray(samples) / float(sr)


class DMImage:
    def __init__(self, app):
        image = Image.open(sim.RESOURCES_DIR / "bluetooth-connect.png")
        image = image.resize((20, 20))
        self.ble_connect_img = ImageTk.PhotoImage(image)
        # image = Image.open(dme.RESOURCES_DIR / "bluetooth-off.png")
        # image = image.resize((20, 20))
        # self.ble_dis_img = ImageTk.PhotoImage(image)
        # image = Image.open(dme.RESOURCES_DIR / "bluetooth-transfer.png")
        # image = image.resize((20, 20))
        # self.ble_connected_img = ImageTk.PhotoImage(image)
        # image = Image.open(dme.RESOURCES_DIR / "lan-disconnect.png")
        # image = image.resize((20, 20))
        # self.ble_disconnected_img = ImageTk.PhotoImage(image)
        # image = Image.open(dme.RESOURCES_DIR / "magnify.png")
        # image = image.resize((20, 20))
        # self.ble_searching = ImageTk.PhotoImage(image)
        # image = Image.open(dme.RESOURCES_DIR / "skip-backward.png")
        # image = image.resize((20, 20))
        # self.step_img_first = ImageTk.PhotoImage(image)
        # image = Image.open(dme.RESOURCES_DIR / "skip-forward.png")
        # image = image.resize((20, 20))
        # self.step_img_last = ImageTk.PhotoImage(image)
        # image = Image.open(dme.RESOURCES_DIR / "skip-next.png")
        # image = image.resize((20, 20))
        # self.step_img_next = ImageTk.PhotoImage(image)
        # image = Image.open(dme.RESOURCES_DIR / "skip-previous.png")
        # image = image.resize((20, 20))
        # self.step_img_previous = ImageTk.PhotoImage(image)
        # image = Image.open(dme.RESOURCES_DIR / "play.png")
        # image = image.resize((20, 20))
        # self.play_img = ImageTk.PhotoImage(image)
        # image = Image.open(dme.RESOURCES_DIR / "pause.png")
        # image = image.resize((20, 20))
        # self.pause_img = ImageTk.PhotoImage(image)
        # image = Image.open(dme.RESOURCES_DIR / "stop.png")
        # image = image.resize((20, 20))
        # self.stop_img = ImageTk.PhotoImage(image)
