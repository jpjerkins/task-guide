import { TasksScreen } from '../components/TasksScreen'
import { registerScreen } from '../components/shared/screenRegistry'

registerScreen({
  id: 'tasks',
  tab: 'tasks',
  title: 'Tasks',
  render: () => <TasksScreen />,
})
