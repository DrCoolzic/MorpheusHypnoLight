# Dream Machine to Morpheus HynoLight converter

We need a python script to convert Dream Machine sequences to Morpheus HypnoLight sequences.

## fields conversion

Sequence

- Convert: version, author, createdAt, name
- Ignore: duration, gradient
- Step
  - Convert: 'timeEnd - timeStart' to duration
  - Ignore: index
  - runtimeType ignored
  - Oscillator (5)
    - Waveform = square
    - Frequency => mode = linear, start = frequencyStart, end = frequencyEnd,
    - brightness=> mode = linear, start = brightnessStart * Bcoef, end = brightnessEnd * Bcoef
    - duty => mode = linear, start = dutyStart, end = dutyEnd,
    - runtimeType ignored
    - Do this for the 4 oscillators. A non specified oscillator or an oscillator with no led convert to an oscillator with brightness mode=static, value=0 
    - Oscillator 5 => brightness mode=static, value=0

## Brightness correction Bcoef

In Dream Machine, each LED has a different brightness factor. We need to compute the average brightness factor for all LEDs in a step.

Brightness correction: we have the following factors for LEDs A1=1, A2=4, A4-A5=2, B1-B4=3
Brightness correction (Bcoef) = sum of led coefficients / 21

Example LED = [“A1”, “A4”, “B3”] => Bcoef = (1 + 2 + 3) / 21 = 0.28
so brightnessStart = 50 becomes start = 14

## File name and location

The file convertion script should take a Dream Machine sequence.jsonfile as input and output a Morpheus HypnoLight sequence file with the same name but with a .mhl extension in the same directory.

The "Programmes" conversion script should take a Dream Machine "Programmes" directory as input. Foreach program directory it sould convert all sequence.json files located in "sequence" subdirectories using the file conversion script.
