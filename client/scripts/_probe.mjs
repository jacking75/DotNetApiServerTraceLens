import cytoscape from 'cytoscape'
const base = {
  'background-fill': 'linear-gradient',
  'background-gradient-direction': 'to-right',
}
function tryStyle(name, style) {
  try {
    cytoscape({ headless: true, styleEnabled: true, style: [{ selector: 'node', style }] }).style()
    console.log('OK  ', name)
  } catch (error) {
    console.log('FAIL', name, '→', String(error).slice(0, 120))
  }
}
tryStyle('stop-colors with data()', { ...base, 'background-gradient-stop-colors': 'data(color) data(color) #101820 #101820' })
tryStyle('stop-colors plain', { ...base, 'background-gradient-stop-colors': '#ff0000 #ff0000 #101820 #101820' })
tryStyle('stop-positions', { ...base, 'background-gradient-stop-positions': '0% 6% 6% 100%' })
tryStyle('background-image-containment', { 'background-image-containment': 'over' })
tryStyle('background-offset', { 'background-offset-x': -4, 'background-offset-y': -4 })
tryStyle('background-clip', { 'background-clip': 'none' })
tryStyle('background-image data()', { 'background-image': 'data(badge)' })
