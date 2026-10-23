"""Ordinary QML controls; passive observations and an optional explicit grabWindow adapter."""
import sys
from pathlib import Path
import json
import os
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / '.deps' / 'python'))
from PySide6.QtCore import QUrl, QTimer, qVersion
from PySide6.QtGui import QGuiApplication
from PySide6.QtQml import QQmlApplicationEngine
from PySide6.QtQuick import QQuickWindow, QSGRendererInterface

app = QGuiApplication(sys.argv)
software = '--software' in sys.argv
if software:
    QQuickWindow.setGraphicsApi(QSGRendererInterface.GraphicsApi.Software)
# Otherwise use ordinary platform/environment selection, exactly as a normal QML app.
engine = QQmlApplicationEngine()
engine.warnings.connect(lambda errors: (Path.cwd() / 'startup-error.txt').write_text('\n'.join(str(error) for error in errors), encoding='utf-8'))
engine.load(QUrl.fromLocalFile(str(Path(__file__).with_suffix('.qml'))))
if not engine.rootObjects():
    raise SystemExit(1)
window = engine.rootObjects()[0]
ticks = 0
frames = 0
def swapped():
    global frames
    frames += 1
window.frameSwapped.connect(swapped)
def write():
    global ticks
    ticks += 1
    data = {k: window.property(k) for k in ['clicks', 'keyEvents', 'lastModifiers', 'wheelEvents', 'contextActions', 'drags', 'dialogState', 'textValue', 'selection', 'focusName', 'scrollOffset', 'shortcuts', 'altShortcuts', 'caret', 'hovered', 'contextOpen']}
    data.update(framework='QtQuick', pid=os.getpid(), ticks=ticks, frames=frames, qt=qVersion(), graphicsApi=str(window.rendererInterface().graphicsApi()), requestedSoftware=software)
    report = Path.cwd() / 'observed.json'
    temp = report.with_suffix('.tmp')
    temp.write_text(json.dumps(data), encoding='utf-8')
    temp.replace(report)
    # This is an explicitly cooperative fixture adapter, not transparent unmodified capture.
    request = Path.cwd() / 'grab.request'
    if '--enable-grab-adapter' in sys.argv and request.exists():
        request.unlink()
        image = window.grabWindow()
        for filename in ['quick-grab.png', 'fixture-grab.png']:
            path = Path.cwd() / filename
            temp_image = path.with_suffix('.tmp')
            if image.save(str(temp_image), 'PNG'):
                temp_image.replace(path)
timer = QTimer()
timer.timeout.connect(write)
timer.start(200)
write()
sys.exit(app.exec())
