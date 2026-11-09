import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import * as commandNames from 'Commands/commandNames';
import withCurrentPage from 'Components/withCurrentPage';
import { clearAlbums, fetchAlbums } from 'Store/Actions/albumActions';
import { executeCommand, executeCommandHelper } from 'Store/Actions/commandActions';
import * as queueActions from 'Store/Actions/queueActions';
import { createCustomFiltersSelector } from 'Store/Selectors/createClientSideCollectionSelector';
import createCommandExecutingSelector from 'Store/Selectors/createCommandExecutingSelector';
import hasDifferentItems from 'Utilities/Object/hasDifferentItems';
import selectUniqueIds from 'Utilities/Object/selectUniqueIds';
import { registerPagePopulator, unregisterPagePopulator } from 'Utilities/pagePopulator';
import Queue from './Queue';

function createMapStateToProps() {
  return createSelector(
    (state) => state.artist,
    (state) => state.albums,
    (state) => state.queue.options,
    (state) => state.queue.paged,
    (state) => state.queue.status.item,
    createCustomFiltersSelector('queue'),
    createCommandExecutingSelector(commandNames.REFRESH_MONITORED_DOWNLOADS),
    createCommandExecutingSelector(commandNames.RETRY_AI_MATCHES),
    (artist, albums, options, queue, status, customFilters, isRefreshMonitoredDownloadsExecuting, isRetryAiMatchesExecuting) => {
      return {
        count: options.includeUnknownArtistItems ? status.totalCount : status.count,
        isArtistFetching: artist.isFetching,
        isArtistPopulated: artist.isPopulated,
        isAlbumsFetching: albums.isFetching,
        isAlbumsPopulated: albums.isPopulated,
        albumsError: albums.error,
        customFilters,
        isRefreshMonitoredDownloadsExecuting,
        isRetryAiMatchesExecuting,
        ...options,
        ...queue
      };
    }
  );
}

const mapDispatchToProps = {
  ...queueActions,
  fetchAlbums,
  clearAlbums,
  executeCommand,
  queueRetryAi: (payload) => (dispatch) => executeCommandHelper(payload, dispatch)
};

class QueueConnector extends Component {

  constructor(props, context) {
    super(props, context);
    this.state = { isQueueingAiRetry: false, aiRetryError: false };
    this._retryPending = false;
    this._isMounted = false;
  }

  //
  // Lifecycle

  componentDidMount() {
    this._isMounted = true;
    const {
      useCurrentPage,
      fetchQueue,
      fetchQueueStatus,
      gotoQueueFirstPage
    } = this.props;

    registerPagePopulator(this.repopulate);

    if (useCurrentPage) {
      fetchQueue();
    } else {
      gotoQueueFirstPage();
    }

    fetchQueueStatus();
  }

  componentDidUpdate(prevProps) {
    if (hasDifferentItems(prevProps.items, this.props.items)) {
      const albumIds = selectUniqueIds(this.props.items, 'albumId');

      if (albumIds.length) {
        this.props.fetchAlbums({ albumIds });
      } else {
        this.props.clearAlbums();
      }
    }

    if (
      this.props.includeUnknownArtistItems !==
      prevProps.includeUnknownArtistItems
    ) {
      this.repopulate();
    }
  }

  componentWillUnmount() {
    this._isMounted = false;
    unregisterPagePopulator(this.repopulate);
    this.props.clearQueue();
    this.props.clearAlbums();
  }

  //
  // Control

  repopulate = () => {
    this.props.fetchQueue();
  };

  //
  // Listeners

  onFirstPagePress = () => {
    this.props.gotoQueueFirstPage();
  };

  onPreviousPagePress = () => {
    this.props.gotoQueuePreviousPage();
  };

  onNextPagePress = () => {
    this.props.gotoQueueNextPage();
  };

  onLastPagePress = () => {
    this.props.gotoQueueLastPage();
  };

  onPageSelect = (page) => {
    this.props.gotoQueuePage({ page });
  };

  onSortPress = (sortKey) => {
    this.props.setQueueSort({ sortKey });
  };

  onFilterSelect = (selectedFilterKey) => {
    this.props.setQueueFilter({ selectedFilterKey });
  };

  onTableOptionChange = (payload) => {
    this.props.setQueueTableOption(payload);

    if (payload.pageSize) {
      this.props.gotoQueueFirstPage();
    }
  };

  onRefreshPress = () => {
    this.props.executeCommand({
      name: commandNames.REFRESH_MONITORED_DOWNLOADS,
      queueOnly: true
    });
  };

  onRetryAiPress = async() => {
    if (this._retryPending || this.props.isRetryAiMatchesExecuting) {
      return;
    }

    this._retryPending = true;
    this.setState({ isQueueingAiRetry: true, aiRetryError: false });
    try {
      await this.props.queueRetryAi({ name: commandNames.RETRY_AI_MATCHES });
    } catch {
      if (this._isMounted) {
        this.setState({ aiRetryError: true });
      }
    } finally {
      this._retryPending = false;
      if (this._isMounted) {
        this.setState({ isQueueingAiRetry: false });
      }
    }
  };

  onGrabSelectedPress = (ids) => {
    this.props.grabQueueItems({ ids });
  };

  onRemoveSelectedPress = (payload) => {
    this.props.removeQueueItems(payload);
  };

  //
  // Render

  render() {
    return (
      <Queue
        onFirstPagePress={this.onFirstPagePress}
        onPreviousPagePress={this.onPreviousPagePress}
        onNextPagePress={this.onNextPagePress}
        onLastPagePress={this.onLastPagePress}
        onPageSelect={this.onPageSelect}
        onSortPress={this.onSortPress}
        onFilterSelect={this.onFilterSelect}
        onTableOptionChange={this.onTableOptionChange}
        onRefreshPress={this.onRefreshPress}
        onRetryAiPress={this.onRetryAiPress}
        onGrabSelectedPress={this.onGrabSelectedPress}
        onRemoveSelectedPress={this.onRemoveSelectedPress}
        {...this.props}
        isRetryAiMatchesExecuting={this.state.isQueueingAiRetry || this.props.isRetryAiMatchesExecuting}
        aiRetryError={this.state.aiRetryError}
      />
    );
  }
}

QueueConnector.propTypes = {
  useCurrentPage: PropTypes.bool.isRequired,
  items: PropTypes.arrayOf(PropTypes.object).isRequired,
  includeUnknownArtistItems: PropTypes.bool.isRequired,
  fetchQueue: PropTypes.func.isRequired,
  fetchQueueStatus: PropTypes.func.isRequired,
  gotoQueueFirstPage: PropTypes.func.isRequired,
  gotoQueuePreviousPage: PropTypes.func.isRequired,
  gotoQueueNextPage: PropTypes.func.isRequired,
  gotoQueueLastPage: PropTypes.func.isRequired,
  gotoQueuePage: PropTypes.func.isRequired,
  setQueueSort: PropTypes.func.isRequired,
  setQueueFilter: PropTypes.func.isRequired,
  setQueueTableOption: PropTypes.func.isRequired,
  clearQueue: PropTypes.func.isRequired,
  grabQueueItems: PropTypes.func.isRequired,
  removeQueueItems: PropTypes.func.isRequired,
  fetchAlbums: PropTypes.func.isRequired,
  clearAlbums: PropTypes.func.isRequired,
  executeCommand: PropTypes.func.isRequired,
  queueRetryAi: PropTypes.func.isRequired,
  isRetryAiMatchesExecuting: PropTypes.bool.isRequired
};

export default withCurrentPage(
  connect(createMapStateToProps, mapDispatchToProps)(QueueConnector)
);
