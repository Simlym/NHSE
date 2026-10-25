import { t } from './i18n';

export function buildingTypeName(type: number): string {
  const names = t('mapBuildingTypes').split('|');
  if (type === 0) return t('mapEmptyBuilding');
  if (type >= 1 && type <= 8) return `${names[0]} ${type}`;
  if (type >= 9 && type <= 18) return `${names[1]} ${type - 8}`;
  return names[type === 42 ? 13 : type - 17] ?? `${t('mapModes').split('|')[2]} ${type}`;
}
