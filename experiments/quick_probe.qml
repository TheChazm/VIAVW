import QtQuick
import QtQuick.Controls
import QtQuick.Window

ApplicationWindow {
    id: root
    width: 620; height: 440; visible: true
    title: "Disposable Qt Quick application"
    color: "#f0f3f7"
    property int clicks: 0
    property int keyEvents: 0
    property int lastModifiers: 0
    property int wheelEvents: 0
    property int contextActions: 0
    property int drags: 0
    property int shortcuts: 0
    property int altShortcuts: 0
    property int caret: editor.cursorPosition
    property bool hovered: counter.hovered
    property bool contextOpen: contextMenu.visible
    Shortcut { sequence: "Ctrl+K"; onActivated: root.shortcuts++ }
    Shortcut { sequence: "Alt+K"; onActivated: root.altShortcuts++ }
    property string dialogState: review.visible ? "open" : "closed"
    property string textValue: editor.text
    property string selection: editor.selectedText
    property string focusName: activeFocusItem ? activeFocusItem.objectName : "none"
    property real scrollOffset: scroll.contentY
    Text { x: 24; y: 20; text: "Ordinary Qt Quick controls — GPU scene graph"; font.pixelSize: 18 }
    TextField {
        id: editor; objectName: "editor"; x: 24; y: 60; width: 570; height: 50
        focus: true; selectByMouse: true
        Keys.onPressed: event => { root.keyEvents++; root.lastModifiers = event.modifiers; event.accepted = false }
        onPressed: event => { if (event.button === Qt.RightButton) contextMenu.popup() }
    }
    Button { id: counter; objectName: "counter"; x: 24; y: 128; width: 200; height: 44; text: "Increment counter"; hoverEnabled: true; onClicked: root.clicks++ }
    Button { objectName: "modal"; x: 24; y: 184; width: 200; height: 38; text: "Open dialog"; onClicked: review.open() }
    Rectangle {
        x: 420; y: 128; width: 150; height: 84; color: root.clicks % 2 ? "#f28c28" : "#28b464"
        DragHandler { onActiveChanged: { if (!active) root.drags++ } }
    }
    Flickable {
        id: scroll; x: 24; y: 238; width: 350; height: 130; contentHeight: 1200; clip: true
        Rectangle { width: 350; height: 1200; color: "white"; Text { x: 10; y: 0; text: Array.from({length: 50}, (_, i) => "Scroll line " + (i+1)).join("\n"); font.pixelSize: 16 } }
        WheelHandler { onWheel: event => { root.wheelEvents++; event.accepted = false } }
    }
    Text { x: 24; y: 390; width: 570; text: "clicks=" + root.clicks + "; keys=" + root.keyEvents + "; text=" + editor.text; font.pixelSize: 14 }
    Menu { id: contextMenu; MenuItem { text: "Mark context action"; onTriggered: root.contextActions++ } }
    Dialog { id: review; title: "Qt Quick modal review"; x: 150; y: 140; width: 320; height: 180; modal: true; standardButtons: Dialog.Ok | Dialog.Cancel; TextField { text: "Modal state" } }
}
