# POS-58 regression fixture

`pos58-v1.bin` was captured by running the original, unmodified renderer and test
suite before the compatibility pass. It is the synthetic receipt with local
finish time September 16, 2026, 7:06 AM. Tests reconstruct that timestamp and
compare the complete command stream byte-for-byte; do not regenerate this fixture
from the new renderer. It is test data only and is never sent to a printer.
