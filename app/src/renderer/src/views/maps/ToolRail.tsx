import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  MousePointer2,
  ImagePlus,
  MapPin,
  Type,
  Spline,
  Trees,
  Building2
} from 'lucide-react'
import {
  BUILDING_TYPES,
  SPLINE_PRESETS,
  TERRAIN_TYPES,
  type MapProfileT,
  type ToolMode
} from './mapModel'

interface ToolRailProps {
  activeTool: ToolMode
  disabled: boolean
  buildingScale: number
  customProfiles: MapProfileT[]
  onSelectTool(tool: ToolMode): void
  onAddImage(): void
  onSplinePreset(kind: string, preset: string): void
  onTerrain(type: string): void
  onBuilding(type: string): void
  onBuildingScale(scale: number): void
}

type FlyoutName = 'spline' | 'terrain' | 'building'

/** Left tool palette that drives map.html's tool modes and element drafts. */
export function ToolRail({
  activeTool,
  disabled,
  buildingScale,
  customProfiles,
  onSelectTool,
  onAddImage,
  onSplinePreset,
  onTerrain,
  onBuilding,
  onBuildingScale
}: ToolRailProps): React.JSX.Element {
  const { t } = useTranslation()
  const [flyout, setFlyout] = useState<FlyoutName | null>(null)
  const railRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!flyout) return
    const onDown = (e: MouseEvent): void => {
      if (railRef.current && !railRef.current.contains(e.target as Node)) setFlyout(null)
    }
    window.addEventListener('mousedown', onDown, true)
    return () => window.removeEventListener('mousedown', onDown, true)
  }, [flyout])

  const roads = SPLINE_PRESETS.filter((p) => p.kind === 'road')
  const rivers = SPLINE_PRESETS.filter((p) => p.kind === 'river')

  return (
    <div className="map-toolrail" ref={railRef}>
      <button
        type="button"
        className={`map-tool${activeTool === 'select' ? ' active' : ''}`}
        title={t('map.bbIdle')}
        disabled={disabled}
        onClick={() => {
          setFlyout(null)
          onSelectTool('select')
        }}
      >
        <MousePointer2 size={16} strokeWidth={2} />
      </button>

      <button
        type="button"
        className="map-tool"
        title={t('map.toolAddImageTooltip')}
        disabled={disabled}
        onClick={() => {
          setFlyout(null)
          onAddImage()
        }}
      >
        <ImagePlus size={16} strokeWidth={2} />
      </button>

      <button
        type="button"
        className={`map-tool${activeTool === 'add-pin' ? ' active' : ''}`}
        title={t('map.toolAddPinTooltip')}
        disabled={disabled}
        onClick={() => {
          setFlyout(null)
          onSelectTool(activeTool === 'add-pin' ? 'select' : 'add-pin')
        }}
      >
        <MapPin size={16} strokeWidth={2} />
      </button>

      <button
        type="button"
        className={`map-tool${activeTool === 'add-label' ? ' active' : ''}`}
        title={t('map.toolAddLabelTooltip')}
        disabled={disabled}
        onClick={() => {
          setFlyout(null)
          onSelectTool(activeTool === 'add-label' ? 'select' : 'add-label')
        }}
      >
        <Type size={16} strokeWidth={2} />
      </button>

      <div className="map-tool-group">
        <button
          type="button"
          className={`map-tool${activeTool === 'spline' ? ' active' : ''}`}
          title={t('map.toolSplineTooltip')}
          disabled={disabled}
          onClick={() => setFlyout(flyout === 'spline' ? null : 'spline')}
        >
          <Spline size={16} strokeWidth={2} />
        </button>
        {flyout === 'spline' && (
          <div className="map-flyout" role="menu">
            <div className="map-flyout-header">{t('map.splineRoadHeader')}</div>
            {roads.map((p) => (
              <button
                key={`${p.kind}:${p.preset}`}
                type="button"
                className="map-flyout-item"
                onClick={() => {
                  setFlyout(null)
                  onSplinePreset(p.kind, p.preset)
                }}
              >
                {t(p.labelKey)}
              </button>
            ))}
            <div className="map-flyout-header">{t('map.splineRiverHeader')}</div>
            {rivers.map((p) => (
              <button
                key={`${p.kind}:${p.preset}`}
                type="button"
                className="map-flyout-item"
                onClick={() => {
                  setFlyout(null)
                  onSplinePreset(p.kind, p.preset)
                }}
              >
                {t(p.labelKey)}
              </button>
            ))}
            {customProfiles.length > 0 && (
              <>
                <div className="map-flyout-header">{t('map.splineCustomHeader')}</div>
                {customProfiles.map((p) => (
                  <button
                    key={p.id}
                    type="button"
                    className="map-flyout-item"
                    onClick={() => {
                      setFlyout(null)
                      onSplinePreset(p.kind || 'road', `custom:${p.id}`)
                    }}
                  >
                    {p.name}
                  </button>
                ))}
              </>
            )}
          </div>
        )}
      </div>

      <div className="map-tool-group">
        <button
          type="button"
          className={`map-tool${activeTool === 'terrain' ? ' active' : ''}`}
          title={t('map.toolTerrainTooltip')}
          disabled={disabled}
          onClick={() => setFlyout(flyout === 'terrain' ? null : 'terrain')}
        >
          <Trees size={16} strokeWidth={2} />
        </button>
        {flyout === 'terrain' && (
          <div className="map-flyout" role="menu">
            {TERRAIN_TYPES.map((tt) => (
              <button
                key={tt.type}
                type="button"
                className="map-flyout-item"
                onClick={() => {
                  setFlyout(null)
                  onTerrain(tt.type)
                }}
              >
                {t(tt.labelKey)}
              </button>
            ))}
          </div>
        )}
      </div>

      <div className="map-tool-group">
        <button
          type="button"
          className={`map-tool${activeTool === 'building' ? ' active' : ''}`}
          title={t('map.toolBuildingTooltip')}
          disabled={disabled}
          onClick={() => setFlyout(flyout === 'building' ? null : 'building')}
        >
          <Building2 size={16} strokeWidth={2} />
        </button>
        {flyout === 'building' && (
          <div className="map-flyout" role="menu">
            {BUILDING_TYPES.map((bt) => (
              <button
                key={bt.type}
                type="button"
                className="map-flyout-item"
                onClick={() => {
                  setFlyout(null)
                  onBuilding(bt.type)
                }}
              >
                {t(bt.labelKey)}
              </button>
            ))}
            <div className="map-flyout-header">{t('map.buildingScale')}</div>
            <div className="map-flyout-scale">
              <input
                type="range"
                min={0.25}
                max={4}
                step={0.25}
                value={buildingScale}
                onChange={(e) => onBuildingScale(Number(e.target.value))}
              />
              <span className="map-scale-value">{buildingScale.toFixed(2)}×</span>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}
