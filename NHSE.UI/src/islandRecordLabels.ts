import { t } from './i18n';
const keys = ['Body', 'Footer', 'Date', 'BuiltDate', 'PlayerName', 'IslandName', 'PlayerID', 'IslandID', 'Gender', 'IslandRubyType', 'DesignId', 'PopId', 'NsaId', 'UniqueId', 'LatestUniqueId', 'HandWrite.Image', 'HandWrite.Palette', 'HandWrite.VerticesNum', 'VisitorNPC', 'InitSchedule', 'IsNormalLive', 'IsBirthdayLive', 'DayWisp', 'DayCeleste', 'CreatePos', '_61e631dc'];
export function islandRecordLabel(key: string) {
  const index = keys.indexOf(key) >= 0 ? keys.indexOf(key) : keys.indexOf(key.split('.')[0]!);
  return index < 0 ? key : t('recordFieldNames').split('|')[index]! + (key.startsWith('CreatePos.') || key.startsWith('_61e631dc.') ? ` · ${key.split('.')[1]}` : '');
}
