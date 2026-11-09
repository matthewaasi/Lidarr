import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import * as commandNames from 'Commands/commandNames';
import { setAlbumsSort, setAlbumsTableOption, toggleAlbumsMonitored } from 'Store/Actions/albumActions';
import { executeCommandHelper } from 'Store/Actions/commandActions';
import createArtistSelector from 'Store/Selectors/createArtistSelector';
import createClientSideCollectionSelector from 'Store/Selectors/createClientSideCollectionSelector';
import createCommandsSelector from 'Store/Selectors/createCommandsSelector';
import createDimensionsSelector from 'Store/Selectors/createDimensionsSelector';
import createUISettingsSelector from 'Store/Selectors/createUISettingsSelector';
import { isCommandExecuting } from 'Utilities/Command';
import ArtistDetailsSeason from './ArtistDetailsSeason';

export function createMapStateToProps() {
  return createSelector(
    (state, { label }) => label,
    createClientSideCollectionSelector('albums'),
    createArtistSelector(),
    createCommandsSelector(),
    createDimensionsSelector(),
    createUISettingsSelector(),
    (label, albums, artist, commands, dimensions, uiSettings) => {
      const albumsByType = albums.items.filter(({ artistId, albumType }) => artistId === artist.id && albumType === label);
      const searchAlbumIds = [...new Set(albumsByType.filter(({ monitored }) => monitored).map(({ id }) => id))];
      const isSearching = commands.some((command) => {
        if (!isCommandExecuting(command)) {
          return false;
        }

        return (command.name === commandNames.ARTIST_SEARCH && command.body?.artistId === artist.id) ||
          (command.name === commandNames.ALBUM_SEARCH && command.body?.albumIds?.some((id) => searchAlbumIds.includes(id)));
      });

      return {
        items: albumsByType,
        searchAlbumIds,
        isSearching,
        columns: albums.columns,
        artistMonitored: artist.monitored,
        sortKey: albums.sortKey,
        sortDirection: albums.sortDirection,
        isSmallScreen: dimensions.isSmallScreen,
        uiSettings
      };
    }
  );
}

function mapDispatchToProps(dispatch) {
  return {
    toggleAlbumsMonitored: (payload) => dispatch(toggleAlbumsMonitored(payload)),
    setAlbumsTableOption: (payload) => dispatch(setAlbumsTableOption(payload)),
    dispatchSetAlbumSort: (payload) => dispatch(setAlbumsSort(payload)),
    queueAlbumSearch: (payload) => executeCommandHelper(payload, dispatch)
  };
}

export class ArtistDetailsSeasonConnector extends Component {

  constructor(props, context) {
    super(props, context);
    this.state = { isQueueingSearch: false, searchError: false };
    this._isQueueingSearch = false;
    this._requestId = 0;
    this._isMounted = false;
  }

  componentDidMount() {
    this._isMounted = true;
  }

  componentDidUpdate(prevProps) {
    if (prevProps.artistId !== this.props.artistId) {
      this._requestId++;
      this._isQueueingSearch = false;
      this.setState({ isQueueingSearch: false, searchError: false });
    }
  }

  componentWillUnmount() {
    this._isMounted = false;
  }

  //
  // Listeners

  onTableOptionChange = (payload) => {
    this.props.setAlbumsTableOption(payload);
  };

  onSortPress = (sortKey) => {
    this.props.dispatchSetAlbumSort({ sortKey });
  };

  onMonitorAlbumsPress = (albumIds, monitored) => {
    this.props.toggleAlbumsMonitored({
      albumIds,
      monitored
    });
  };

  onSearchPress = async() => {
    const { searchAlbumIds, isSearching } = this.props;
    if (this._isQueueingSearch || isSearching || !searchAlbumIds.length) {
      return;
    }

    this._isQueueingSearch = true;
    const requestId = ++this._requestId;
    this.setState({ isQueueingSearch: true, searchError: false });
    try {
      await this.props.queueAlbumSearch({
        name: commandNames.ALBUM_SEARCH,
        albumIds: searchAlbumIds
      });
    } catch {
      if (this._isMounted && requestId === this._requestId) {
        this.setState({ searchError: true });
      }
    } finally {
      if (requestId === this._requestId) {
        this._isQueueingSearch = false;
        if (this._isMounted) {
          this.setState({ isQueueingSearch: false });
        }
      }
    }
  };

  //
  // Render

  render() {
    return (
      <ArtistDetailsSeason
        {...this.props}
        isSearching={this.state.isQueueingSearch || this.props.isSearching}
        searchError={this.state.searchError}
        onSearchPress={this.onSearchPress}
        onSortPress={this.onSortPress}
        onTableOptionChange={this.onTableOptionChange}
        onMonitorAlbumsPress={this.onMonitorAlbumsPress}
      />
    );
  }
}

ArtistDetailsSeasonConnector.propTypes = {
  artistId: PropTypes.number.isRequired,
  toggleAlbumsMonitored: PropTypes.func.isRequired,
  setAlbumsTableOption: PropTypes.func.isRequired,
  dispatchSetAlbumSort: PropTypes.func.isRequired,
  queueAlbumSearch: PropTypes.func.isRequired,
  searchAlbumIds: PropTypes.arrayOf(PropTypes.number).isRequired,
  isSearching: PropTypes.bool.isRequired
};

export default connect(createMapStateToProps, mapDispatchToProps)(ArtistDetailsSeasonConnector);
