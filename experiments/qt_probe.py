"""Ordinary Qt Widgets fixture with passive event/telemetry observation.

No LayerHost imports, RPC endpoints, input replacement, or native-child forcing.
Run with the project-local PySide6 Essentials runtime prepared by setup-qt.ps1.
"""
import json
from pathlib import Path
import sys
import os

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / '.deps' / 'python'))
from PySide6.QtCore import QEvent, QObject, QTimer, qVersion
from PySide6.QtWidgets import QApplication, QWidget, QVBoxLayout, QLabel, QLineEdit, QPushButton


app = QApplication(sys.argv)
window = QWidget()
window.setWindowTitle('Disposable ordinary Qt Widgets app')
window.resize(620, 330)
layout = QVBoxLayout(window)
layout.setContentsMargins(24, 24, 24, 24)
layout.addWidget(QLabel('Qt Widgets: ordinary raster window and standard event handling'))
editor = QLineEdit()
editor.setMinimumHeight(48)
layout.addWidget(editor)
button = QPushButton('Increment counter')
button.setMinimumHeight(48)
layout.addWidget(button)
status = QLabel()
layout.addWidget(status)
state = dict(ticks=0, clicks=0, keyPresses=0, mousePresses=0, pid=os.getpid(), qt=qVersion())


class Observer(QObject):
    def eventFilter(self, obj, event):
        if event.type() == QEvent.Type.KeyPress:
            state['keyPresses'] += 1
        if event.type() == QEvent.Type.MouseButtonPress:
            state['mousePresses'] += 1
        return False  # Ordinary Qt handling continues unchanged.


observer = Observer()
editor.installEventFilter(observer)
button.installEventFilter(observer)


def update():
    state['ticks'] += 1
    state['text'] = editor.text()
    state['focusWidget'] = type(app.focusWidget()).__name__
    status.setText(f"ticks={state['ticks']}; clicks={state['clicks']}; keys={state['keyPresses']}; text={state['text']}")
    report = Path.cwd() / 'qt-observed.json'
    temp = report.with_suffix('.tmp')
    temp.write_text(json.dumps(state), encoding='utf-8')
    temp.replace(report)


def clicked():
    state['clicks'] += 1


button.clicked.connect(clicked)
timer = QTimer()
timer.timeout.connect(update)
timer.start(250)
window.show()
editor.setFocus()
update()
sys.exit(app.exec())
