import { t } from './i18n';
const keys = [
  'HouseLevel',
  'HouseStatus',
  'WallUniqueID',
  'RoofUniqueID',
  'DoorUniqueID',
  'OrderWallUniqueID',
  'OrderRoofUniqueID',
  'OrderDoorUniqueID',
  'Loan',
  'Unknown',
  'NPC1',
  'NPC2',
  'BuildPlayer',
  'PlayerHouseFlag',
  'Flag',
  'DoorDecoItemName',
  'PostItemName',
  'OrderPostItemName',
  'ExtraItem',
  'ItemId',
  'SystemParam',
  'AdditionalParam',
  'FreeParam',
  'Occupied',
  'VisitorCode',
  'VisitDate',
  'ProfileFruit',
  'SpecialtyFruit',
  'SisterFruit',
  'SpecialtyFlower',
  'SisterFlower',
];
export function housingLabel(key: string): string {
  if (key.startsWith('Map.')) {
    const part = key.slice(4);
    const mapKeys = ['UnitModel', 'Variation', 'LandMakingAngle', 'UnitModelRoad', 'VariationRoad', 'LandMakingAngleRoad', 'Elevation', 'BuildingType', 'X', 'Y', 'Angle', 'Bit', 'Type', 'TypeArg', 'UniqueID', 'Unused', 'AcreTemplate', 'PlazaX', 'PlazaY', 'OutsideTemplate', 'MainTemplate', 'DesignValue'];
    const index = mapKeys.indexOf(part);
    return index >= 0 ? t('mapFieldNames').split('|')[index]! : housingLabel(part);
  }
  if (key.startsWith('EventFlag.')) return `${t('housingEventFlag')} #${key.split('.')[1]}`;
  const names = t('housingFieldNames').split('|');
  return key
    .split('.')
    .map((part) => names[keys.indexOf(part)] ?? part)
    .join(' · ');
}
export const housingKinds = ['playerHouses', 'residentHouses', 'campsite', 'fruitFlowers'] as const;
export function housingName(kind: string) {
  return t('housingNames').split('|')[housingKinds.indexOf(kind as (typeof housingKinds)[number])] ?? kind;
}
