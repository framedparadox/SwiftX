# XML validate: the original and the fixed code, back to back

`PerformanceBudgetTests` (Debug), the original `HEAD` and the working tree run one after the other on the
same machine in the same minute, so what differs is the code and not the machine's mood. Debug only: the
original code was not run in Release, and Debug exaggerates the cost of building a tree.

| Measure | Original | Fixed |
|---|---:|---:|
| `xml validate`, 3.7 MB payload | 44.1 ms | 27.1 ms |
| `xml validate`, 10 000 -> 80 000 rows | 3.9 -> 98.5 ms (**x25.1** for an x8 input) | 1.6 -> 13.5 ms (**x8.3**) |
| `xml format`, 10 000 -> 80 000 rows | 8.8 -> 143.1 ms (x16.2) | 8.7 -> 123.7 ms (x14.2) |
| `xml to canonical`, 10 000 -> 80 000 rows | 25.3 -> 332.0 ms (x13.1) | 35.7 -> 247.0 ms (x6.9) |
| Allocated by `Validate` on the 3.7 MB payload | 18 207 504 bytes (the tree) | **11 496 bytes** |

Only `validate` changed; the other two rows are the same code measured twice and show the spread to expect
(x13.1 vs x6.9 for canonical). The allocation figure is thread-local and does not depend on load, which is
why `AllocationBudgetTests` asserts on it instead of on milliseconds.

The very first baseline (a different, busier moment: CPU gate at 24%, an OS update half way through the
session) read `xml validate` x27.7 and `xml format` x30.1 against a ceiling of x32 - near the limit, and
the reason the finding is rated "watch" rather than "fine".
