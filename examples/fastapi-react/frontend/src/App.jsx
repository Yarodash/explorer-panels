import { useEffect, useState } from 'react'

export default function App() {
  const [hello, setHello] = useState(null)
  const [notes, setNotes] = useState([])
  const [text, setText] = useState('')
  const [error, setError] = useState(null)

  async function load() {
    try {
      const [h, n] = await Promise.all([
        fetch('/api/hello').then((r) => r.json()),
        fetch('/api/notes').then((r) => r.json()),
      ])
      setHello(h)
      setNotes(n)
      setError(null)
    } catch {
      setError('Сервер не отвечает — запусти backend')
    }
  }

  useEffect(() => {
    load()
  }, [])

  async function addNote(e) {
    e.preventDefault()
    if (!text.trim()) return
    const res = await fetch('/api/notes', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ text }),
    })
    if (res.ok) {
      const note = await res.json()
      setNotes((prev) => [...prev, note])
      setText('')
    }
  }

  async function removeNote(id) {
    const res = await fetch(`/api/notes/${id}`, { method: 'DELETE' })
    if (res.ok) setNotes((prev) => prev.filter((n) => n.id !== id))
  }

  return (
    <main>
      <h1>⚛️ Сайт на React</h1>
      {error && <p className="error">{error}</p>}
      {hello && (
        <p className="muted">
          {hello.message} · {hello.time}
        </p>
      )}

      <form onSubmit={addNote}>
        <input value={text} onChange={(e) => setText(e.target.value)} placeholder="Новая заметка" />
        <button type="submit">Добавить</button>
      </form>

      <ul>
        {notes.map((n) => (
          <li key={n.id}>
            <span>{n.text}</span>
            <button className="del" onClick={() => removeNote(n.id)} aria-label="Удалить">
              ✕
            </button>
          </li>
        ))}
      </ul>

      <p className="muted">
        <a href="http://localhost:8000">Python-сайт</a> · <a href="http://localhost:8000/docs">Swagger API</a>
      </p>
    </main>
  )
}
