import { createSelector, createSelectorCreator, defaultMemoize } from 'reselect';
import createClientSideCollectionSelector from './createClientSideCollectionSelector';

function createUnoptimizedSelector(uiSection) {
  return createSelector(
    createClientSideCollectionSelector('artist', uiSection),
    (artist) => {
      const items = artist.items.map((s) => {
        const {
          id,
          sortName
        } = s;

        return {
          id,
          sortName
        };
      });

      return {
        ...artist,
        items
      };
    }
  );
}

function artistListEqual(a, b) {
  if (a === b) {
    return true;
  }

  const keys = Object.keys(a);
  return keys.length === Object.keys(b).length && keys.every((key) => {
    if (key !== 'items') {
      return a[key] === b[key];
    }

    return a.items.length === b.items.length && a.items.every((item, index) => (
      item.id === b.items[index].id && item.sortName === b.items[index].sortName
    ));
  });
}

const createArtistEqualSelector = createSelectorCreator(
  defaultMemoize,
  artistListEqual
);

function createArtistClientSideCollectionItemsSelector(uiSection) {
  return createArtistEqualSelector(
    createUnoptimizedSelector(uiSection),
    (artist) => artist
  );
}

export default createArtistClientSideCollectionItemsSelector;
