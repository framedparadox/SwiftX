# Live app measurements

- Exe: `%USERPROFILE%\AppData\Local\Temp\claude\c--Git-SwiftX\1efb7ac8-d270-4159-abb1-9c64fc9234e0\scratchpad\perf-review\workspace\src\SwiftX\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\SwiftX.exe`
- Machine: 13th Gen Intel(R) Core(TM) i5-1334U, 12 logical cores, 15.7 GB, power plan 'Balanced', on battery = False
- Commit: 852cf87 (51 uncommitted paths); user's own SwiftX PIDs at run time: 

## Findings
- summon : handles GROWING at 92.69 per 100 cycles (early 1459 -> late 1564.5)
- settings : UI thread stalled up to 482.1 ms (budget 250 ms, 29 stall(s))
- settings : privMb GROWING at 70.09 per 100 cycles (early 155.3 -> late 175.1)
- settings : wsMb GROWING at 97.59 per 100 cycles (early 255.4 -> late 282.2)
- settings : handles GROWING at 3359.3 per 100 cycles (early 1677 -> late 2625.5)
- settings : gdi GROWING at 2580.56 per 100 cycles (early 348 -> late 1077)
- activation : privMb GROWING at 43.29 per 100 cycles (early 131 -> late 147.5)
- activation : wsMb GROWING at 47.6 per 100 cycles (early 228.2 -> late 246.4)
- spike : UI thread stalled up to 318.9 ms
- unreachable : UI thread stalled 316.5 ms in summonUiThread with an unreachable network target on the orbit

```json
{
  "startup": {
    "runs": [
      {
        "run": 1,
        "trayReadyMs": 968.3,
        "settingsVisibleMs": 1701.4,
        "cpuQuietAfterSec": 23.9,
        "cpuSecondsToSettle": 6.72,
        "peakWsMb": 209.3,
        "wsMb": 203.1,
        "privMb": 105.8,
        "handles": 1380,
        "gdi": 129,
        "user": 45,
        "threads": 38,
        "crashed": false,
        "exitCode": null,
        "newDumps": null,
        "appLogFaults": [
          "08:13:32.020 IconService: favicon fetch failed for 'http://127.0.0.1:9/favicon.ico': HttpRequestException: No connection could be made because the target machine actively refused it. (127.0.0.1:9)",
          "08:13:32.158 IconService: favicon fetch failed for 'http://127.0.0.1:9/favicon.ico': HttpRequestException: No connection could be made because the target machine actively refused it. (127.0.0.1:9)"
        ]
      },
      {
        "run": 2,
        "trayReadyMs": 547.6,
        "settingsVisibleMs": 1080.3,
        "cpuQuietAfterSec": 23.3,
        "cpuSecondsToSettle": 5.58,
        "peakWsMb": 207.4,
        "wsMb": 200.8,
        "privMb": 104.9,
        "handles": 1368,
        "gdi": 129,
        "user": 44,
        "threads": 38,
        "crashed": false,
        "exitCode": null,
        "newDumps": null,
        "appLogFaults": [
          "08:13:56.698 IconService: favicon fetch failed for 'http://127.0.0.1:9/favicon.ico': HttpRequestException: No connection could be made because the target machine actively refused it. (127.0.0.1:9)",
          "08:13:56.878 IconService: favicon fetch failed for 'http://127.0.0.1:9/favicon.ico': HttpRequestException: No connection could be made because the target machine actively refused it. (127.0.0.1:9)"
        ]
      },
      {
        "run": 3,
        "trayReadyMs": 497.1,
        "settingsVisibleMs": 1161.6,
        "cpuQuietAfterSec": 23.3,
        "cpuSecondsToSettle": 6.06,
        "peakWsMb": 207.9,
        "wsMb": 202.7,
        "privMb": 106.7,
        "handles": 1380,
        "gdi": 133,
        "user": 46,
        "threads": 40,
        "crashed": false,
        "exitCode": null,
        "newDumps": null,
        "appLogFaults": [
          "08:14:21.892 IconService: favicon fetch failed for 'http://127.0.0.1:9/favicon.ico': HttpRequestException: No connection could be made because the target machine actively refused it. (127.0.0.1:9)",
          "08:14:22.027 IconService: favicon fetch failed for 'http://127.0.0.1:9/favicon.ico': HttpRequestException: No connection could be made because the target machine actively refused it. (127.0.0.1:9)"
        ]
      },
      {
        "run": 4,
        "trayReadyMs": 644.5,
        "settingsVisibleMs": 1451.7,
        "cpuQuietAfterSec": 23.6,
        "cpuSecondsToSettle": 6.94,
        "peakWsMb": 207.8,
        "wsMb": 202.0,
        "privMb": 107.0,
        "handles": 1379,
        "gdi": 127,
        "user": 46,
        "threads": 40,
        "crashed": false,
        "exitCode": null,
        "newDumps": null,
        "appLogFaults": [
          "08:14:47.186 IconService: favicon fetch failed for 'http://127.0.0.1:9/favicon.ico': HttpRequestException: No connection could be made because the target machine actively refused it. (127.0.0.1:9)",
          "08:14:47.282 IconService: favicon fetch failed for 'http://127.0.0.1:9/favicon.ico': HttpRequestException: No connection could be made because the target machine actively refused it. (127.0.0.1:9)"
        ]
      },
      {
        "run": 5,
        "trayReadyMs": 609.1,
        "settingsVisibleMs": 1296.4,
        "cpuQuietAfterSec": 23.5,
        "cpuSecondsToSettle": 5.83,
        "peakWsMb": 207.5,
        "wsMb": 201.1,
        "privMb": 105.6,
        "handles": 1367,
        "gdi": 123,
        "user": 45,
        "threads": 39,
        "crashed": false,
        "exitCode": null,
        "newDumps": null,
        "appLogFaults": [
          "08:15:12.350 IconService: favicon fetch failed for 'http://127.0.0.1:9/favicon.ico': HttpRequestException: No connection could be made because the target machine actively refused it. (127.0.0.1:9)",
          "08:15:12.527 IconService: favicon fetch failed for 'http://127.0.0.1:9/favicon.ico': HttpRequestException: No connection could be made because the target machine actively refused it. (127.0.0.1:9)"
        ]
      }
    ],
    "coldSettingsVisibleMs": 1701.4,
    "warmSettingsVisibleMedianMs": 1229.0,
    "warmSettingsVisibleMaxMs": 1451.7,
    "warmTrayReadyMedianMs": 578.4,
    "medianPrivMb": 105.8,
    "medianHandles": 1379.0,
    "phaseSeconds": 126.0
  },
  "idle": {
    "default": {
      "variant": "default",
      "seconds": 30,
      "cpuPercentOfOneCore": 0.371,
      "cpuPercentOfMachine": 0.031,
      "wsMbStart": 144.7,
      "wsMbEnd": 138.7,
      "privMbStart": 79.2,
      "privMbEnd": 72.6,
      "handles": 887,
      "threads": 22,
      "gdi": 57,
      "user": 25,
      "crashed": false,
      "exitCode": null,
      "appLogFaults": null
    },
    "hotCornerOff": {
      "variant": "hotcorner-off",
      "seconds": 30,
      "cpuPercentOfOneCore": 0.0,
      "cpuPercentOfMachine": 0.0,
      "wsMbStart": 146.2,
      "wsMbEnd": 138.3,
      "privMbStart": 81.4,
      "privMbEnd": 73.8,
      "handles": 885,
      "threads": 22,
      "gdi": 57,
      "user": 24,
      "crashed": false,
      "exitCode": null,
      "appLogFaults": null
    },
    "settingsOpen": {
      "variant": "settings-open",
      "seconds": 30,
      "cpuPercentOfOneCore": 0.479,
      "cpuPercentOfMachine": 0.04,
      "wsMbStart": 200.4,
      "wsMbEnd": 198.8,
      "privMbStart": 105.6,
      "privMbEnd": 103.8,
      "handles": 1347,
      "threads": 37,
      "gdi": 115,
      "user": 44,
      "crashed": false,
      "exitCode": null,
      "appLogFaults": [
        "08:17:03.681 IconService: favicon fetch failed for 'http://127.0.0.1:9/favicon.ico': HttpRequestException: No connection could be made because the target machine actively refused it. (127.0.0.1:9)",
        "08:17:03.761 IconService: favicon fetch failed for 'http://127.0.0.1:9/favicon.ico': HttpRequestException: No connection could be made because the target machine actively refused it. (127.0.0.1:9)"
      ]
    },
    "hotCornerCostPercentOfOneCore": 0.371,
    "budgetPercentOfOneCore": 1.0,
    "phaseSeconds": 144.0
  },
  "summon": {
    "cyclesRequested": 150,
    "cyclesCompleted": 150,
    "failures": 0,
    "summonMs": {
      "p50": 15.8,
      "p95": 19.7,
      "max": 30.7,
      "first": 16.1
    },
    "dismissMs": {
      "p50": 470.8,
      "p95": 483.8,
      "max": 679.2
    },
    "uiThread": {
      "pings": 3851,
      "p50Ms": 1.2,
      "p95Ms": 10.5,
      "p99Ms": 32.8,
      "maxMs": 216.4,
      "timeouts": 0,
      "stalls": 0,
      "longestStallMs": 0.0,
      "stallBudgetMs": 250,
      "stallList": []
    },
    "crashed": false,
    "exitCode": null,
    "newDumps": null,
    "appLogFaults": [
      "08:18:06.631 IconService: favicon fetch failed for 'http://127.0.0.1:9/favicon.ico': HttpRequestException: No connection could be made because the target machine actively refused it. (127.0.0.1:9)",
      "08:18:07.195 IconService: favicon fetch failed for 'http://127.0.0.1:9/favicon.ico': HttpRequestException: No connection could be made because the target machine actively refused it. (127.0.0.1:9)",
      "08:18:08.079 IconService: favicon fetch failed for 'http://127.0.0.1:9/favicon.ico': HttpRequestException: No connection could be made because the target machine actively refused it. (127.0.0.1:9)",
      "08:18:08.947 IconService: favicon fetch failed for 'http://127.0.0.1:9/favicon.ico': HttpRequestException: No connection could be made because the target machine actively refused it. (127.0.0.1:9)"
    ],
    "drift": {
      "privMb": {
        "slopePer100": -1.43,
        "early": 107.0,
        "late": 105.8,
        "delta": -1.2,
        "samples": 26
      },
      "wsMb": {
        "slopePer100": -1.24,
        "early": 197.9,
        "late": 197.0,
        "delta": -1.0,
        "samples": 26
      },
      "handles": {
        "slopePer100": 92.69,
        "early": 1459.0,
        "late": 1564.5,
        "delta": 105.5,
        "samples": 26
      },
      "gdi": {
        "slopePer100": 0.0,
        "early": 98.0,
        "late": 98.0,
        "delta": 0.0,
        "samples": 26
      },
      "user": {
        "slopePer100": -0.12,
        "early": 38.0,
        "late": 38.0,
        "delta": 0.0,
        "samples": 26
      },
      "threads": {
        "slopePer100": -9.73,
        "early": 35.0,
        "late": 27.5,
        "delta": -7.5,
        "samples": 26
      }
    },
    "driftVerdict": {
      "privMb": "stable",
      "wsMb": "stable",
      "handles": "GROWING",
      "gdi": "stable",
      "user": "stable",
      "threads": "stable"
    },
    "samples": [...],
    "phaseSeconds": 139.0
  },
  "spike": {
    "togglesSent": 300,
    "burstSeconds": 12.2,
    "recoveredOpen": true,
    "recoveredClose": true,
    "recoveryOpenMs": 10.2,
    "recoveryCloseMs": 466.6,
    "uiThread": {
      "pings": 491,
      "p50Ms": 0.6,
      "p95Ms": 8.6,
      "p99Ms": 57.4,
      "maxMs": 318.9,
      "timeouts": 0,
      "stalls": 1,
      "longestStallMs": 318.9,
      "stallBudgetMs": 250,
      "stallList": [
        {
          "atSec": 0.0,
          "ms": 318.9
        }
      ]
    },
    "counters": {
      "t": 17.15,
      "wsMb": 189.6,
      "privMb": 98.8,
      "handles": 1398,
      "threads": 38,
      "gdi": 98,
      "user": 40,
      "cpuSec": 6.797
    },
    "crashed": false,
    "exitCode": null,
    "newDumps": null,
    "appLogFaults": [
      "08:20:25.975 IconService: favicon fetch failed for 'http://127.0.0.1:9/favicon.ico': HttpRequestException: No connection could be made because the target machine actively refused it. (127.0.0.1:9)",
      "08:20:26.928 IconService: favicon fetch failed for 'http://127.0.0.1:9/favicon.ico': HttpRequestException: No connection could be made because the target machine actively refused it. (127.0.0.1:9)",
      "08:20:27.908 IconService: favicon fetch failed for 'http://127.0.0.1:9/favicon.ico': HttpRequestException: No connection could be made because the target machine actively refused it. (127.0.0.1:9)"
    ],
    "phaseSeconds": 24.0
  },
  "settings": {
    "keptRunningPid": null,
    "cyclesRequested": 40,
    "cyclesCompleted": 40,
    "failures": 0,
    "windowVisibleMs": {
      "p50": 237.5,
      "p95": 353.6,
      "max": 420.6,
      "first": 317.5
    },
    "interactiveMs": {
      "p50": 610.6,
      "p95": 843.8,
      "max": 918.6
    },
    "closeMs": {
      "p50": 34.2,
      "p95": 63.3
    },
    "uiThread": {
      "pings": 1182,
      "p50Ms": 0.4,
      "p95Ms": 106.5,
      "p99Ms": 379.1,
      "maxMs": 482.1,
      "timeouts": 0,
      "stalls": 29,
      "longestStallMs": 482.1,
      "stallBudgetMs": 250,
      "stallList": [
        {
          "atSec": 0.0,
          "ms": 390.1
        },
        {
          "atSec": 3.0,
          "ms": 398.0
        },
        {
          "atSec": 4.5,
          "ms": 344.8
        },
        {
          "atSec": 5.9,
          "ms": 431.8
        },
        {
          "atSec": 8.8,
          "ms": 377.6
        },
        {
          "atSec": 10.2,
          "ms": 432.0
        },
        {
          "atSec": 13.1,
          "ms": 409.4
        },
        {
          "atSec": 14.7,
          "ms": 257.7
        },
        {
          "atSec": 16.1,
          "ms": 337.6
        },
        {
          "atSec": 17.6,
          "ms": 297.8
        },
        {
          "atSec": 19.0,
          "ms": 379.1
        },
        {
          "atSec": 20.5,
          "ms": 399.2
        },
        {
          "atSec": 23.2,
          "ms": 259.2
        },
        {
          "atSec": 24.5,
          "ms": 407.1
        },
        {
          "atSec": 28.7,
          "ms": 281.7
        }
      ]
    },
    "crashed": false,
    "exitCode": null,
    "newDumps": null,
    "appLogFaults": [
      "08:20:49.850 IconService: favicon fetch failed for 'http://127.0.0.1:9/favicon.ico': HttpRequestException: No connection could be made because the target machine actively refused it. (127.0.0.1:9)",
      "08:20:49.926 IconService: favicon fetch failed for 'http://127.0.0.1:9/favicon.ico': HttpRequestException: No connection could be made because the target machine actively refused it. (127.0.0.1:9)",
      "08:20:51.077 IconService: favicon fetch failed for 'http://127.0.0.1:9/favicon.ico': HttpRequestException: No connection could be made because the target machine actively refused it. (127.0.0.1:9)",
      "08:20:51.229 IconService: favicon fetch failed for 'http://127.0.0.1:9/favicon.ico': HttpRequestException: No connection could be made because the target machine actively refused it. (127.0.0.1:9)"
    ],
    "drift": {
      "privMb": {
        "slopePer100": 70.09,
        "early": 155.3,
        "late": 175.1,
        "delta": 19.8,
        "samples": 18
      },
      "wsMb": {
        "slopePer100": 97.59,
        "early": 255.4,
        "late": 282.2,
        "delta": 26.9,
        "samples": 18
      },
      "handles": {
        "slopePer100": 3359.3,
        "early": 1677.0,
        "late": 2625.5,
        "delta": 948.5,
        "samples": 18
      },
      "gdi": {
        "slopePer100": 2580.56,
        "early": 348.0,
        "late": 1077.0,
        "delta": 729.0,
        "samples": 18
      },
      "user": {
        "slopePer100": 0.25,
        "early": 39.0,
        "late": 39.0,
        "delta": 0.0,
        "samples": 18
      },
      "threads": {
        "slopePer100": -4.19,
        "early": 46.0,
        "late": 44.0,
        "delta": -2.0,
        "samples": 18
      }
    },
    "driftVerdict": {
      "privMb": "GROWING",
      "wsMb": "GROWING",
      "handles": "GROWING",
      "gdi": "GROWING",
      "user": "stable",
      "threads": "stable"
    },
    "samples": [...],
    "phaseSeconds": 66.0
  },
  "activation": {
    "pairs": 60,
    "crashed": false,
    "exitCode": null,
    "newDumps": null,
    "appLogFaults": [
      "08:21:54.972 IconService: favicon fetch failed for 'http://127.0.0.1:9/favicon.ico': HttpRequestException: No connection could be made because the target machine actively refused it. (127.0.0.1:9)",
      "08:21:55.096 IconService: favicon fetch failed for 'http://127.0.0.1:9/favicon.ico': HttpRequestException: No connection could be made because the target machine actively refused it. (127.0.0.1:9)",
      "08:22:58.009 IconService: favicon fetch failed for 'http://127.0.0.1:9/favicon.ico': HttpRequestException: No connection could be made because the target machine actively refused it. (127.0.0.1:9)",
      "08:22:59.142 IconService: favicon fetch failed for 'http://127.0.0.1:9/favicon.ico': HttpRequestException: No connection could be made because the target machine actively refused it. (127.0.0.1:9)",
      "08:23:00.260 IconService: favicon fetch failed for 'http://127.0.0.1:9/favicon.ico': HttpRequestException: No connection could be made because the target machine actively refused it. (127.0.0.1:9)"
    ],
    "drift": {
      "privMb": {
        "slopePer100": 43.29,
        "early": 131.0,
        "late": 147.5,
        "delta": 16.6,
        "samples": 6
      },
      "wsMb": {
        "slopePer100": 47.6,
        "early": 228.2,
        "late": 246.4,
        "delta": 18.2,
        "samples": 6
      },
      "handles": {
        "slopePer100": -12.29,
        "early": 1531.5,
        "late": 1518.5,
        "delta": -13.0,
        "samples": 6
      },
      "gdi": {
        "slopePer100": -0.57,
        "early": 110.0,
        "late": 111.0,
        "delta": 1.0,
        "samples": 6
      },
      "user": {
        "slopePer100": -3.71,
        "early": 48.5,
        "late": 47.0,
        "delta": -1.5,
        "samples": 6
      },
      "threads": {
        "slopePer100": -8.57,
        "early": 42.5,
        "late": 39.0,
        "delta": -3.5,
        "samples": 6
      }
    },
    "driftVerdict": {
      "privMb": "GROWING",
      "wsMb": "GROWING",
      "handles": "stable",
      "gdi": "stable",
      "user": "stable",
      "threads": "stable"
    },
    "samples": [...],
    "gdiPerActivationPair": -0.41,
    "handlesPerActivationPair": 2.02,
    "phaseSeconds": 78.0
  },
  "unreachable": {
    "messageWindowReadyMs": 574.2,
    "stalledStillAfter12sMs": 0.0,
    "startupUiThread": {
      "pings": 363,
      "p50Ms": 0.3,
      "p95Ms": 0.7,
      "p99Ms": 1.8,
      "maxMs": 51.8,
      "timeouts": 0,
      "stalls": 0,
      "longestStallMs": 0.0,
      "stallBudgetMs": 250,
      "stallList": []
    },
    "startupResponsiveAgain": true,
    "firstSummonVisibleMs": 16.8,
    "stalledStill3sAfterSummonMs": 0.3,
    "summonResponsiveAgain": true,
    "summonUiThread": {
      "pings": 78,
      "p50Ms": 0.6,
      "p95Ms": 3.3,
      "p99Ms": 316.5,
      "maxMs": 316.5,
      "timeouts": 0,
      "stalls": 1,
      "longestStallMs": 316.5,
      "stallBudgetMs": 250,
      "stallList": [
        {
          "atSec": 0.0,
          "ms": 316.5
        }
      ]
    },
    "crashed": false,
    "exitCode": null,
    "newDumps": null,
    "appLogFaults": "08:23:20.239 IconService: favicon fetch failed for 'http://127.0.0.1:9/favicon.ico': HttpRequestException: No connection could be made because the target machine actively refused it. (127.0.0.1:9)",
    "phaseSeconds": 16.0
  }
}
```

