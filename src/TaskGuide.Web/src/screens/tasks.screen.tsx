import { TasksScreen } from '../components/TasksScreen'
import { registerScreen } from '../components/shared/screenRegistry'

// Vite only recognizes a direct accept call in this module. Registration keeps the paired,
// id-scoped dispose callback at the existing registration seam.
if (import.meta.hot) {
  import.meta.hot.accept()
}

registerScreen({
  id: 'tasks',
  tab: 'tasks',
  title: 'Tasks',
  render: () => <TasksScreen />,
}, import.meta.hot)
