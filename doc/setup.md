# Setup environment

```bash
# create virtual environment
python -m venv .venv
# activate virtual environment
.\.venv\Scripts\Activate.ps1
# upgrade pip
python -m pip install --upgrade pip
#install packages
pip install jupyter numpy matplotlib plotly ipywidgets pandas ipykernel
# create jupyter kernel
python -m ipykernel install --user --name=morpheus-hypnolight --display-name="Python (MorpheusHypnoLight)"
pip list
```
