import { useState } from 'react'
import { PlaceholderScreen } from './components/PlaceholderScreen'
import { TabBar, type Tab } from './components/TabBar'
import { TasksScreen } from './components/TasksScreen'

export default function App() {
  const [tab, setTab] = useState<Tab>('tasks')

  return (
    <div className="device">
      {tab === 'now' && <PlaceholderScreen title="Now" />}
      {tab === 'tasks' && <TasksScreen />}
      {tab === 'schedule' && <PlaceholderScreen title="Schedule" />}
      {tab === 'more' && <PlaceholderScreen title="More" />}
      <TabBar active={tab} onChange={setTab} />
    </div>
  )
}
