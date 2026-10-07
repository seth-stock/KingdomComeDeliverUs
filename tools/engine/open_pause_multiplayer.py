"""From the verified ESC menu's New Game row, open Multiplayer; no game/save action selected."""
import engine_session
engine_session.verified()
for _ in range(5): engine_session.key('Down')
engine_session.key('Enter')
