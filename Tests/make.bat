set /p fname="Test Name: "
copy NUL %fname%.amta
copy NUL %fname%.out
code %fname%.amta %fname%.out