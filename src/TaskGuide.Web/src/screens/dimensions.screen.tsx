import { DimensionsScreen } from '../components/DimensionsScreen'
import { registerScreen } from '../components/shared/screenRegistry'

if (import.meta.hot) {
  import.meta.hot.accept()
}

registerScreen(
  {
    id: 'dimensions',
    tab: 'more',
    title: 'Dimensions',
    render: () => <DimensionsScreen />,
  },
  import.meta.hot,
)
