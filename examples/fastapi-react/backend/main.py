from datetime import datetime

from fastapi import FastAPI, HTTPException
from fastapi.responses import HTMLResponse
from pydantic import BaseModel

app = FastAPI(title="Simple Notes API")

# Хранилище в памяти — при перезапуске всё обнуляется
notes: list[dict] = [
    {"id": 1, "text": "Привет из Python!", "created": datetime.now().isoformat(timespec="seconds")},
]
next_id = 2


class NoteIn(BaseModel):
    text: str


@app.get("/api/hello")
def hello():
    return {"message": "Сервер на FastAPI живой", "time": datetime.now().isoformat(timespec="seconds")}


@app.get("/api/notes")
def list_notes():
    return notes


@app.post("/api/notes", status_code=201)
def add_note(note: NoteIn):
    global next_id
    text = note.text.strip()
    if not text:
        raise HTTPException(status_code=400, detail="Пустая заметка")
    item = {"id": next_id, "text": text, "created": datetime.now().isoformat(timespec="seconds")}
    next_id += 1
    notes.append(item)
    return item


@app.delete("/api/notes/{note_id}", status_code=204)
def delete_note(note_id: int):
    for i, n in enumerate(notes):
        if n["id"] == note_id:
            notes.pop(i)
            return
    raise HTTPException(status_code=404, detail="Нет такой заметки")


# "Сайт на питоне" — страница, которую рендерит сам сервер
@app.get("/", response_class=HTMLResponse)
def index():
    from html import escape

    items = "".join(f"<li>{escape(n['text'])} <small>{n['created']}</small></li>" for n in notes)
    return f"""<!doctype html>
<html lang="ru">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Python-сайт</title>
  <style>
    body {{ font-family: system-ui, sans-serif; max-width: 640px; margin: 40px auto; padding: 0 16px; background: #f4f4f8; }}
    h1 {{ color: #3776ab; }}
    li {{ margin: 6px 0; }}
    small {{ color: #888; }}
    form {{ display: flex; gap: 8px; margin-top: 16px; }}
    input {{ flex: 1; padding: 8px; }}
  </style>
</head>
<body>
  <h1>🐍 Сайт на Python (FastAPI)</h1>
  <p>Эта страница отрендерена на сервере. Заметок: {len(notes)}</p>
  <ul>{items}</ul>
  <form onsubmit="event.preventDefault(); fetch('/api/notes', {{method:'POST', headers:{{'Content-Type':'application/json'}}, body: JSON.stringify({{text: this.t.value}})}}).then(() => location.reload())">
    <input name="t" placeholder="Новая заметка" required>
    <button>Добавить</button>
  </form>
  <p><a href="/docs">Swagger API</a> · <a href="http://localhost:5173">React-сайт</a></p>
</body>
</html>"""
