import { useState } from 'react'
import { PlaceholderScreen } from './components/PlaceholderScreen'
import { TabBar, type Tab } from './components/TabBar'
import { installHmrGuard, screensFor } from './components/shared/screenRegistry'
import { BackProvider, ScreenNav } from './components/shared/ScreenNav'

// Each file under ./screens registers itself as a module side effect (registerScreen). This is
// the ONLY place that needs to know the directory exists — a new Web ticket adds its own
// `<name>.screen.tsx` file there and this glob picks it up with zero edits here.
import.meta.glob('./screens/**/*.screen.tsx', { eager: true })

// Screen modules accept and replace only their own registration. An App.tsx edit cannot safely
// rebuild every eager screen import from the browser cache, so its HMR disposal requests a full
// reload rather than silently rendering placeholders from an empty registry.
installHmrGuard(import.meta.hot)

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
      // Every screen renders its own ScreenNav (the prototype's nav() is called by each screen,
      // never by the shell — docs/prototypes/ui-screens.prototype.html), so the shell must not
      // wrap it in a second one. The back action still needs to reach that screen's ScreenNav
      // without it knowing it was reached via an index, so it travels through context instead.
      content = <BackProvider value={{ onBack: () => setSelectedId(null) }}>{selected.render()}</BackProvider>
    } else {
      content = (
        <>
          <ScreenNav title={TAB_TITLES[tab]} />
          <div className="scroll">
            <div className="list">
              {screens.map((s) => (
                <button key={s.id} className="row" onClick={() => setSelectedId(s.id)}>
                  <span className="body title">{s.title}</span>
                </button>
              ))}
            </div>
          </div>
        </>
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
