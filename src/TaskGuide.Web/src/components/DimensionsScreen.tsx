import { useEffect, useState } from 'react'
import { fetchDimensions, type DimensionResponse } from '../api/client'
import { OrdinalSlider } from './shared/OrdinalSlider'
import { ScreenNav } from './shared/ScreenNav'

type LoadState =
  | { status: 'loading' }
  | { status: 'error' }
  | { status: 'ready'; dimensions: DimensionResponse[] }

function DimensionRow({ dimension }: { dimension: DimensionResponse }) {
  const ordinal = dimension.algebra === 'ordinal'
  const defaultValue = dimension.taskDefault ?? dimension.windowDefault

  return (
    <div className="row" data-dimension-id={dimension.id}>
      <div className="body">
        <div className="title">{dimension.label}</div>
        <div className="meta">
          <span className="pill">{ordinal ? 'ordinal — ceiling' : 'categorical — subset'}</span>
          <span className="pill dim">{dimension.source}</span>
          {ordinal ? (
            dimension.taskDefault !== null || dimension.windowDefault !== null ? (
              <>
                {dimension.taskDefault !== null && (
                  <span className="pill dim">task default: {dimension.taskDefault}</span>
                )}
                {dimension.windowDefault !== null && (
                  <span className="pill dim">window default: {dimension.windowDefault}</span>
                )}
              </>
            ) : (
              <span className="pill dim">window value derived from its length</span>
            )
          ) : (
            <span className="pill dim">no defaults — absence is the empty set</span>
          )}
        </div>
        {ordinal ? (
          <OrdinalSlider
            label={dimension.label}
            values={dimension.values}
            value={null}
            defaultValue={defaultValue}
            onChange={() => {}}
            readOnly
            id={`${dimension.id}-value`}
          />
        ) : (
          <div className="meta chipset" aria-label={`${dimension.label} values`}>
            {dimension.values.map((value) => (
              <span className="pill" key={value}>
                {value}
              </span>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}

export function DimensionsScreen() {
  const [state, setState] = useState<LoadState>({ status: 'loading' })

  useEffect(() => {
    let current = true
    fetchDimensions()
      .then((dimensions) => {
        if (current) setState({ status: 'ready', dimensions })
      })
      .catch(() => {
        if (current) setState({ status: 'error' })
      })

    return () => {
      current = false
    }
  }, [])

  return (
    <>
      <ScreenNav title="Dimensions" sub="Read-only — declared in code" />
      <div className="scroll">
        {state.status === 'loading' && <div className="empty">Loading…</div>}
        {state.status === 'error' && (
          <div className="empty">Couldn&apos;t load dimensions. Check your connection and try again.</div>
        )}
        {state.status === 'ready' &&
          (state.dimensions.length === 0 ? (
            <div className="empty">No dimensions are declared.</div>
          ) : (
            <>
              <div className="list">
                {state.dimensions.map((dimension) => (
                  <DimensionRow key={dimension.id} dimension={dimension} />
                ))}
              </div>
              <div className="note">
                Two algebras. <b>Ordinal</b> axes compare a task against the window&apos;s ceiling.{' '}
                <b>Categorical</b> axes ask whether the task&apos;s values are a <b>subset</b> of the
                window&apos;s — window values OR together, task values AND together. Absence is the empty
                set, which is why categorical axes need no defaults: an untagged task is a subset of
                everything.
              </div>
              <div className="note">
                There is deliberately <b>no UI for managing dimensions</b>. Adding one is a code change.
                This screen exists so a wrong reminder can be debugged.
              </div>
            </>
          ))}
      </div>
    </>
  )
}
