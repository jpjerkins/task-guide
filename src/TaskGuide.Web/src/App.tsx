import { useState } from 'react'
import { PlaceholderScreen } from './components/PlaceholderScreen'
import { TabBar, type Tab } from './components/TabBar'
import { screensFor } from './components/shared/screenRegistry'

// Each file under ./screens registers itself as a module side effect (registerScreen). This is
// the ONLY place that needs to know the directory exists — a new Web ticket adds its own
// `<name>.screen.tsx` file there and this glob picks it up with zero edits here.
import.meta.glob('./screens/**/*.screen.tsx', { eager: true })

const TAB_TITLES: Record<Tab, string> = {
  now: 'Now',
  tasks: 'Tasks',
  schedule: 'Schedule',
  more: 'More',
}

export default function App() {
  const [tab, setTab] = useState<Tab>('tasks')
  const [selectedId, setSelectedId] = useState<string | null>(null)

  const screens = screensFor(tab)

  function changeTab(next: Tab) {
    setTab(next)
    setSelectedId(null)
  }

  let content
  if (screens.length === 0) {
    content = <PlaceholderScreen title={TAB_TITLES[tab]} />
  } else if (screens.length === 1) {
    content = screens[0].render()
  } else {
    const selected = screens.find((s) => s.id === selectedId)
    if (selected) {
      content = (
        <div className="nav">
          <div className="nav-main">
            <button className="icon" onClick={() => setSelectedId(null)}>
              ← Back
            </button>
          </div>
          <div className="scroll">{selected.render()}</div>
        </div>
      )
    } else {
      content = (
        <div className="nav">
          <div className="nav-main">
            <h1>{TAB_TITLES[tab]}</h1>
          </div>
          <div className="scroll">
            <div className="list">
              {screens.map((s) => (
                <button key={s.id} className="row" onClick={() => setSelectedId(s.id)}>
                  <span className="body title">{s.title}</span>
                </button>
              ))}
            </div>
          </div>
        </div>
      )
    }
  }

  return (
    <div className="device">
      {content}
      <TabBar active={tab} onChange={changeTab} />
    </div>
  )
}
