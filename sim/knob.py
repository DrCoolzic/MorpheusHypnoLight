# Install tkdial first:
# pip install tkdial
# https://github.com/Akascape/TkDial

import tkinter as tk
from tkdial import Dial

def on_knob_change(value):
    """Callback when knob value changes."""
    label_var.set(f"Value: {int(value)}")

# Create main window
root = tk.Tk()
root.title("Tkinter Knob Example")
root.geometry("300x300")

# Variable to display knob value
label_var = tk.StringVar(value="Value: 0")

# Create a Dial (knob) widget
knob = Dial(
    root,
    start=0,          # Minimum value
    end=100,          # Maximum value
    # unit="%",         # Unit to display
    radius=80,        # Size of the knob
    text_color="black",
    # fg="#4CAF50",     # Foreground color
    bg="#DDDDDD",     # Background color
    needle_color="red",
    command=on_knob_change  # Function to call on change
)
knob.pack(pady=20)

# Label to show current value
label = tk.Label(root, textvariable=label_var, font=("Arial", 14))
label.pack()

root.mainloop()
